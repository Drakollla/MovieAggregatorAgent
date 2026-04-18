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
                "Ты — продвинутый кино-ассистент.\n\n" +
                "У тебя есть инструменты для поиска фильмов:\n" +
                "1. SearchMovie — точный поиск по названию.\n" +
                "2. SearchByCriteria — поиск по критериям (жанр, год, рейтинг).\n\n" +
                "ВАЖНО: Когда пользователь описывает фильм словами — ОБЯЗАТЕЛЬНО вызови SearchByCriteria!\n\n" +
                "Примеры:\n" +
                "- 'мрачный триллер из 90-х с рейтингом 7+' → SearchByCriteria(genre='thriller', yearFrom=1990, yearTo=1999, ratingMin=7)\n" +
                "- 'комедия 2020' → SearchByCriteria(genre='comedy', yearFrom=2020, yearTo=2020)\n\n" +
                "Действуй по примеру выше!");

            var settings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.2
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

                    _logger.LogInformation("LLM Response: {Content}", response.Content);

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