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

            history.AddSystemMessage(@"You are an AI assistant. You must use tools to answer questions.
                You have access to the following tool:
                - Name: WebSearchPlugin-SearchOnline
                - Parameter: query (string)

                INSTRUCTIONS:
                If the user asks about factual information, Oscars, actors, or movie details, YOU MUST CALL the tool 'WebSearchPlugin-SearchOnline' to search Google.
                Do not write any text. Only generate the tool call.");

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