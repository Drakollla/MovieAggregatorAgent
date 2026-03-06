using Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MovieAgentCLI.Services
{
    public class ConsoleChatService : BackgroundService
    {
        private readonly IMovieService _movieService;
        private readonly ILogger<ConsoleChatService> _logger;

        public ConsoleChatService(IMovieService movieService, ILogger<ConsoleChatService> logger)
        {
            _movieService = movieService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("App Started");

            try
            {
                _logger.LogInformation("Ищем Интерстеллар...");
                string jsonResult = await _movieService.SearchMovieAsync("Интерстеллар");

                Console.WriteLine($"Получен ответ: {jsonResult.Substring(0, Math.Min(jsonResult.Length, 500))}...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при поиске фильма");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}