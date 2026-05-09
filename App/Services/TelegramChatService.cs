using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MovieAgentCLI.Services
{
    public class TelegramChatService : BackgroundService
    {
        private readonly Kernel _kernel;
        private readonly ILogger<TelegramChatService> _logger;
        private readonly IChatCompletionService _chatService;
        private readonly ITelegramBotClient _botClient;

        private const int MaxHistoryMessages = 4;

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
            try
            {
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
                        text: "🤖 *Модель задумалась, но вернула пустой ответ. Перефразируйте запрос.*",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                history.AddAssistantMessage(response.Content);
                TrimHistory(history);

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: response.Content,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка чата");
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "❌ Произошла ошибка при обращении к модели. Попробуйте еще раз.",
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