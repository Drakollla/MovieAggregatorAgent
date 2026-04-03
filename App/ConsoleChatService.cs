using Contracts;
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
        private readonly IMovieService _movieService;
        private readonly ILogger<ConsoleChatService> _logger;

        public ConsoleChatService(
            Kernel kernel,
            IMovieService movieService,
            ILogger<ConsoleChatService> logger)
        {
            _kernel = kernel;
            _movieService = movieService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FilmAgent запущен и готов к общению!");
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddSystemMessage(
                "Ты — эксперт-киноман. Твои правила:\n" +
                "1. Если ты получил список фильмов, выведи его пользователю и спроси, о каком рассказать подробнее.\n" +
                "2. Если пользователь выбрал фильм по номеру, найди его название в истории чата и вызови функцию поиска С ЭТИМ НАЗВАНИЕМ.\n" +
                "3. Отвечай всегда на русском языке.");

            var settings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0
            };

            while (!stoppingToken.IsCancellationRequested)
            {
                Console.Write("\nВы: ");
                string? userInput = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(userInput))
                    continue;

                history.AddUserMessage(userInput);

                try
                {
                    var response = await chatService.GetChatMessageContentAsync(
                        history,
                        executionSettings: settings,
                        kernel: _kernel, // Передаем ядро, чтобы модель могла вызывать плагины
                        cancellationToken: stoppingToken
                    );

                    Console.WriteLine($"\nFilmAgent: {response.Content}");

                    history.AddAssistantMessage(response.Content!);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка чата");
                    Console.WriteLine("Произошла ошибка, попробуйте еще раз.");
                }
            }
        }
    }
}