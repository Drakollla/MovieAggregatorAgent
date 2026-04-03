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
                "Ты — продвинутый кино-ассистент. \n" +
                "У тебя есть два инструмента: \n" +
                "1. MoviePlugin-SearchMovie — используй для поиска фактов о фильмах (сюжет, год).\n" +
                "2. WebSearchPlugin-SearchOnline — используй для поиска НОВОСТЕЙ, дат выхода новых сезонов и того, чего может не быть в базе.\n" +
                "Если тебя спрашивают о будущем или о последних новостях — иди в интернет!");

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
                        kernel: _kernel,
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