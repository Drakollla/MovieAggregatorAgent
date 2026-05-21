using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MovieAgentCLI.Services
{
    public class TelegramChatService : BackgroundService
    {
        private readonly Kernel _kernel;
        private readonly ILogger<TelegramChatService> _logger;
        private readonly IChatCompletionService _chatService;
        private readonly ITelegramBotClient _botClient;

        private static readonly Regex CalendarLinkRegex = new(@"\[.*?\]\((https://calendar\.google\.com[^\)]+)\)", RegexOptions.Compiled);
        private static readonly Regex KinopoiskLinkRegex = new(@"https://(?:www\.)?kinopoisk\.ru/(?:film|series)/\d+/?", RegexOptions.Compiled);
        private static readonly Regex LeftoverTextRegex = new(@"(?i)(Где смотреть|Где посмотреть|Ссылка)[\s:]*", RegexOptions.Compiled);
        private static readonly Regex MultipleNewlinesRegex = new(@"\n{3,}", RegexOptions.Compiled);
        private static readonly Regex MoviesTagRegex = new(@"\[MOVIES:(\d+(?:,\d+)*)\]", RegexOptions.Compiled);
        private static readonly Regex MovieTagRegex = new(@"\[MOVIE:(\d+)\]", RegexOptions.Compiled);

        private const int MaxHistoryMessages = 4;
        private const string MovieCallbackPrefix = "movie:";

        private static readonly Dictionary<string, string> StatusMessages = new()
        {
            { "searching", "🔍 Ищу фильм..." },
            { "thinking", "🤔 Анализирую запрос..." },
            { "generating", "✨ Генерирую ответ..." },
            { "details", "📋 Загружаю подробности..." },
        };

        public TelegramChatService(Kernel kernel, ILogger<TelegramChatService> logger, ITelegramBotClient botClient)
        {
            _kernel = kernel;
            _logger = logger;
            _botClient = botClient;
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var me = await _botClient.GetMe(stoppingToken);
            _logger.LogInformation($"FilmAgent (@{me.Username}) запущен и готов к общению!");

            var history = await InitializeChatHistoryAsync();
            var executionSettings = GetExecutionSettings();

            int offset = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var updates = await _botClient.GetUpdates(
                        offset: offset,
                        timeout: 50,
                        cancellationToken: stoppingToken);

                    foreach (var update in updates)
                    {
                        offset = update.Id + 1;

                        await ProcessUpdateAsync(update, history, executionSettings, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при получении обновлений от Telegram");
                    await Task.Delay(2000, stoppingToken);
                }
            }
        }

        #region TelegramChat

        private async Task ProcessUpdateAsync(Update update, ChatHistory history, OpenAIPromptExecutionSettings settings, CancellationToken cancellationToken)
        {
            if (update.CallbackQuery is { } callbackQuery)
            {
                await ProcessCallbackQueryAsync(callbackQuery, history, settings, cancellationToken);
                return;
            }

            if (update.Message is not { Text: { } userInput } message)
                return;

            long chatId = message.Chat.Id;

            if (userInput.StartsWith("/start"))
            {
                var newHistory = await InitializeChatHistoryAsync();
                history.Clear();
                foreach (var msg in newHistory) history.Add(msg);

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "Привет! Я FilmAgent 🎬. Какой фильм вы ищете сегодня?",
                    cancellationToken: cancellationToken);
                return;
            }

            history.AddUserMessage(userInput);

            await _botClient.SendChatAction(
                chatId: chatId,
                action: ChatAction.Typing,
                cancellationToken: cancellationToken);

            await ProcessAgentResponseAsync(chatId, history, settings, cancellationToken);
        }

        private async Task ProcessAgentResponseAsync(long chatId, ChatHistory history, OpenAIPromptExecutionSettings settings, CancellationToken cancellationToken)
        {
            Message? statusMessage = null;

            try
            {
                statusMessage = await SendStatusAsync(chatId, "thinking", cancellationToken);

                var response = await _chatService.GetChatMessageContentAsync(
                    history,
                    executionSettings: settings,
                    kernel: _kernel,
                    cancellationToken: cancellationToken
                );

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "Модель задумалась, но вернула пустой ответ. Перефразируйте запрос.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                await UpdateStatusAsync(chatId, statusMessage.MessageId, "generating", cancellationToken);

                history.AddAssistantMessage(response.Content);
                TrimHistory(history);

                var (finalText, keyboard) = ParseResponseContent(response.Content);

                await SendMessageSafeAsync(chatId, finalText, keyboard, cancellationToken);
                await DeleteStatusAsync(chatId, statusMessage.MessageId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка чата");
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "Произошла ошибка при обращении к модели. Попробуйте еще раз.",
                    cancellationToken: cancellationToken);
                
                if (statusMessage != null)
                    await DeleteStatusAsync(chatId, statusMessage.MessageId, cancellationToken);
            }
        }

        private async Task ProcessCallbackQueryAsync(CallbackQuery callbackQuery, ChatHistory history, OpenAIPromptExecutionSettings settings, CancellationToken cancellationToken)
        {
            if (!TryParseMovieRequest(callbackQuery, out long chatId, out int movieId))
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Ошибка: неверные данные", cancellationToken: cancellationToken);
                return;
            }

            await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            await HandleMovieSelectionAsync(chatId, movieId, history, settings, cancellationToken);
        }

        private bool TryParseMovieRequest(CallbackQuery query, out long chatId, out int movieId)
        {
            movieId = 0;
            chatId = query.Message?.Chat.Id ?? 0;

            if (chatId == 0 || string.IsNullOrEmpty(query.Data) || !query.Data.StartsWith(MovieCallbackPrefix))
                return false;

            string movieIdStr = query.Data[MovieCallbackPrefix.Length..];

            return int.TryParse(movieIdStr, out movieId);
        }

        private async Task HandleMovieSelectionAsync(long chatId, int movieId, ChatHistory history, OpenAIPromptExecutionSettings settings, CancellationToken cancellationToken)
        {
            Message? statusMessage = null;

            try
            {
                statusMessage = await SendStatusAsync(chatId, "details", cancellationToken);
                string? aiResponse = await FetchMovieInfoFromAiAsync(movieId, history, settings, cancellationToken);

                if (string.IsNullOrWhiteSpace(aiResponse))
                {
                    await _botClient.SendMessage(chatId, "Не удалось получить информацию о фильме.", cancellationToken: cancellationToken);
                    return;
                }

                await UpdateStatusAsync(chatId, statusMessage.MessageId, "generating", cancellationToken);

                history.AddAssistantMessage(aiResponse);
                TrimHistory(history);

                var (finalText, keyboard) = ParseResponseContent(aiResponse);
                await SendMessageSafeAsync(chatId, finalText, keyboard, cancellationToken);
                await DeleteStatusAsync(chatId, statusMessage.MessageId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке выбора фильма");
                await _botClient.SendMessage(chatId, "Ошибка при получении информации о фильме.", cancellationToken: cancellationToken);
                
                if (statusMessage != null)
                    await DeleteStatusAsync(chatId, statusMessage.MessageId, cancellationToken);
            }
        }

        private async Task<string?> FetchMovieInfoFromAiAsync(int movieId, ChatHistory history, OpenAIPromptExecutionSettings settings, CancellationToken cancellationToken)
        {
            history.AddUserMessage($"Покажи подробности о фильме с ID {movieId}");

            var response = await _chatService.GetChatMessageContentAsync(
                history,
                executionSettings: settings,
                kernel: _kernel,
                cancellationToken: cancellationToken
            );

            string? content = response.Content;

            if (!string.IsNullOrWhiteSpace(content))
            {
                history.AddAssistantMessage(content);
                TrimHistory(history);
            }

            return content;
        }

        private async Task<Message> SendStatusAsync(long chatId, string statusKey, CancellationToken cancellationToken)
        {
            string text = StatusMessages.GetValueOrDefault(statusKey, "⏳ Обрабатываю...");
            return await _botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
        }

        private async Task UpdateStatusAsync(long chatId, int messageId, string statusKey, CancellationToken cancellationToken)
        {
            string text = StatusMessages.GetValueOrDefault(statusKey, "⏳ Обрабатываю...");
            await _botClient.EditMessageText(chatId, messageId, text, cancellationToken: cancellationToken);
        }

        private async Task DeleteStatusAsync(long chatId, int messageId, CancellationToken cancellationToken)
        {
            try
            {
                await _botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete status message {MessageId}", messageId);
            }
        }

        private (string Text, InlineKeyboardMarkup? Keyboard) ParseResponseContent(string rawText)
        {
            bool isListResponse = MoviesTagRegex.Match(rawText).Success;
            
            if (!isListResponse)
            {
                var linkMatches = KinopoiskLinkRegex.Matches(rawText);
                if (linkMatches.Count > 1)
                    isListResponse = true;
            }

            if (!isListResponse)
            {
                var singleMovieMatches = MovieTagRegex.Matches(rawText);
                if (singleMovieMatches.Count > 1)
                    isListResponse = true;
            }

            string textToParse = rawText;
            if (isListResponse)
            {
                textToParse = MovieTagRegex.Replace(rawText, "").Trim();
            }

            var (text, inlineKeyboard) = ExtractButtons(textToParse);

            text = LeftoverTextRegex.Replace(text, "");
            text = text.Replace("[]", "").Replace("()", "").Replace("[]()", "");
            text = MultipleNewlinesRegex.Replace(text, "\n\n").Trim();

            var keyboard = inlineKeyboard.Count > 0 ? new InlineKeyboardMarkup(inlineKeyboard) : null;

            return (text, keyboard);
        }

        private (string Text, List<InlineKeyboardButton[]> Keyboards) ExtractButtons(string text)
        {
            var keyboard = new List<InlineKeyboardButton[]>();

            text = ExtractCalendarButton(text, keyboard);
            text = ExtractMovieButtons(text, keyboard);
            text = ExtractKinopoiskButtons(text, keyboard);

            return (text, keyboard);
        }

        private string ExtractCalendarButton(string text, List<InlineKeyboardButton[]> keyboard)
        {
            var match = CalendarLinkRegex.Match(text);

            if (match.Success)
            {
                string url = match.Groups[1].Value;
                keyboard.Add([InlineKeyboardButton.WithUrl("Добавить в Календарь", url)]);

                return text.Replace(match.Value, "").Trim();
            }

            return text;
        }

        private string ExtractMovieButtons(string text, List<InlineKeyboardButton[]> keyboard)
        {
            var moviesMatch = MoviesTagRegex.Match(text);

            if (moviesMatch.Success)
            {
                var buttons = moviesMatch.Groups[1].Value.Split(',')
                    .Select(id => id.Trim())
                    .Where(id => int.TryParse(id, out _))
                    .Select(id => InlineKeyboardButton.WithCallbackData("Фильм", $"movie:{id}"));

                keyboard.AddRange(buttons.Chunk(2));

                return MoviesTagRegex.Replace(text, "").Trim();
            }

            var movieMatch = MovieTagRegex.Match(text);

            if (movieMatch.Success && int.TryParse(movieMatch.Groups[1].Value, out int id))
            {
                keyboard.Add([InlineKeyboardButton.WithCallbackData("Обновить данные", $"movie:{id}")]);
                return MovieTagRegex.Replace(text, "").Trim();
            }

            return text;
        }

        private string ExtractKinopoiskButtons(string text, List<InlineKeyboardButton[]> keyboard)
        {
            var kpMatches = KinopoiskLinkRegex.Matches(text);
            var uniqueUrls = kpMatches.Cast<Match>()
                                      .Select(m => m.Value)
                                      .Distinct()
                                      .ToList();

            if (uniqueUrls.Count == 0)
                return text;

            var buttonsRow = new List<InlineKeyboardButton>();

            for (int i = 0; i < uniqueUrls.Count; i++)
            {
                string url = uniqueUrls[i];
                string buttonText = uniqueUrls.Count == 1 ? "Смотреть на Кинопоиске" : $"Фильм {i + 1}";
                buttonsRow.Add(InlineKeyboardButton.WithUrl(buttonText, url));

                string pattern = $@"\[.*?\]\({Regex.Escape(url)}\)";
                text = Regex.Replace(text, pattern, "");
                text = text.Replace(url, "");
            }

            keyboard.Add(buttonsRow.ToArray());
            return text;
        }

        private async Task SendMessageSafeAsync(long chatId, string text, InlineKeyboardMarkup? keyboard, CancellationToken cancellationToken)
        {
            try
            {
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: text,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: cancellationToken);
            }
            catch (ApiRequestException)
            {
                _logger.LogWarning("Telegram не смог распарсить Markdown. Отправляем как обычный текст.");
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: text,
                    replyMarkup: keyboard,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: cancellationToken);
            }
        }

        private void TrimHistory(ChatHistory history)
        {
            if (history.Count <= MaxHistoryMessages + 1)
                return;

            while (history.Count > MaxHistoryMessages + 1)
                history.RemoveAt(1);

            while (history.Count > 1 && history[1].Role == AuthorRole.Tool)
                history.RemoveAt(1);
        }

        #endregion

        #region Initialization
        private async Task<ChatHistory> InitializeChatHistoryAsync()
        {
            string currentDateInfo = GenerateCurrentDateInfo();
            string promptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prompts", "SystemPrompt.md");

            string promptTemplate = await File.ReadAllTextAsync(promptPath);
            string finalSystemPrompt = promptTemplate.Replace("{{CURRENT_DATE_INFO}}", currentDateInfo);

            var history = new ChatHistory();
            history.AddSystemMessage(finalSystemPrompt);

            return history;
        }

        private static OpenAIPromptExecutionSettings GetExecutionSettings()
        {
            return new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.2
            };
        }

        private static string GenerateCurrentDateInfo()
        {
            DateTime today = DateTime.Now;

            return $@"CURRENT DATE INFO:
                - Today: {today:yyyy-MM-dd} ({today.DayOfWeek})
                - Tomorrow: {today.AddDays(1):yyyy-MM-dd}
                - This Friday: {GetNextWeekday(today, DayOfWeek.Friday):yyyy-MM-dd}
                - This Saturday: {GetNextWeekday(today, DayOfWeek.Saturday):yyyy-MM-dd}
                - This Sunday: {GetNextWeekday(today, DayOfWeek.Sunday):yyyy-MM-dd}";
        }

        private static DateTime GetNextWeekday(DateTime start, DayOfWeek day)
        {
            int daysToAdd = ((int)day - (int)start.DayOfWeek + 7) % 7;
            return daysToAdd == 0 ? start.AddDays(7) : start.AddDays(daysToAdd);
        }

        #endregion
    }
}