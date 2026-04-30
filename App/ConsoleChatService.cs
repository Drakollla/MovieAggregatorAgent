using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace MovieAgentCLI.Services
{
    public class ConsoleChatService : BackgroundService
    {
        private readonly Kernel _kernel;
        private readonly ILogger<ConsoleChatService> _logger;
        private readonly IChatCompletionService _chatService;

        private const int MaxHistoryMessages = 10;

        public ConsoleChatService(Kernel kernel, ILogger<ConsoleChatService> logger)
        {
            _kernel = kernel;
            _logger = logger;
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FilmAgent запущен и готов к общению!");

            var history = await InitializeChatHistoryAsync();
            var executionSettings = GetExecutionSettings();

            while (!stoppingToken.IsCancellationRequested)
            {
                string? userInput = GetUserMessage();
                
                if (string.IsNullOrWhiteSpace(userInput))
                    continue;

                history.AddUserMessage(userInput);

                await ProcessAgentResponseAsync(history, executionSettings, stoppingToken);
            }
        }

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

        #region Chat Processing

        private static string? GetUserMessage()
        {
            Console.ResetColor();
            Console.Write("\nВы: ");

            return Console.ReadLine();
        }

        private async Task ProcessAgentResponseAsync(ChatHistory history, OpenAIPromptExecutionSettings settings, CancellationToken cancellationToken)
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
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[СИСТЕМА] Модель задумалась, но вернула пустой ответ. Перефразируйте запрос.");
                    Console.ResetColor();
                    return;
                }

                PrintAgentMessage(response.Content);
                history.AddAssistantMessage(response.Content);

                TrimHistory(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка чата");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nПроизошла ошибка, попробуйте еще раз.");
                Console.ResetColor();
            }
        }

        private static void PrintAgentMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nFilmAgent: {message}");
            Console.ResetColor();
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
    }
}