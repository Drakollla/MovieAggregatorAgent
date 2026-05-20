using Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MovieAgentCLI.Services
{
    public class KinopoiskService : IMovieService
    {
        private const string KinopoiskBaseUrl = "https://api.poiskkino.dev/";
        private const int FutureYearsOffset = 10;

        private readonly HttpClient _httpClient;
        private readonly ILogger<KinopoiskService> _logger;
        private readonly Random _random;

        public KinopoiskService(HttpClient httpClient, IConfiguration config, ILogger<KinopoiskService> logger)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(KinopoiskBaseUrl);
            _logger = logger;
            _random = new Random();

            var token = config["ApiKeys:Tmdb"]
                ?? throw new ArgumentNullException("API Token not found");

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", token);
        }

        public async Task<string> SearchMovieAsync(string query)
        {
            string url = $"v1.4/movie/search?page=1&limit=5&query={Uri.EscapeDataString(query)}";
            return await GetAsync(url);
        }

        public async Task<string> GetMovieByIdAsync(int id)
        {
            string url = $"v1.4/movie/{id}";
            return await GetAsync(url);
        }

        public async Task<string> SearchByCriteriaAsync(
            string? genre = null, int? yearFrom = null,
            int? yearTo = null, float? ratingMin = null,
            string? keyword = null)
        {
            string queryParams = BuildCriteriaQuery(genre, yearFrom, yearTo, ratingMin);
            int randomPage = _random.Next(1, 4);
            string url = $"v1.4/movie?page={randomPage}&limit=10&{queryParams}&sortField=votes.kp&sortType=-1";

            return await GetAsync(url);
        }

        private async Task<string> GetAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Kinopoisk API error ({StatusCode}): {Error}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Kinopoisk API error: {(int)response.StatusCode}");
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Kinopoisk HTTP request failed");
                return CreateErrorJson("Не удалось подключиться к серверу Кинопоиска. Проверьте интернет-соединение.");
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Kinopoisk request timed out");
                return CreateErrorJson("Превышено время ожидания. Попробуйте ещё раз.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Kinopoisk service");
                return CreateErrorJson($"Произошла ошибка: {ex.Message}");
            }
        }

        private static string CreateErrorJson(string message) =>
            JsonSerializer.Serialize(new { error = message });

        private static string BuildCriteriaQuery(string? genre, int? yearFrom, int? yearTo, float? ratingMin)
        {
            var queryParams = new List<string>();

            if (yearFrom.HasValue && yearTo.HasValue)
            {
                queryParams.Add($"year={yearFrom}-{yearTo}");
            }
            else if (yearFrom.HasValue)
            {
                int futureYear = DateTime.Now.Year + FutureYearsOffset;
                queryParams.Add($"year={yearFrom}-{futureYear}");
            }
            else if (yearTo.HasValue)
            {
                queryParams.Add($"year=1890-{yearTo}");
            }

            if (ratingMin.HasValue)
            {
                string ratingStr = ratingMin.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                queryParams.Add($"rating.kp={ratingStr}-10");
            }

            if (!string.IsNullOrEmpty(genre))
            {
                var genresList = genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                queryParams.Add($"genres.name={Uri.EscapeDataString(genresList[0].ToLower())}");
            }

            return string.Join("&", queryParams);
        }
    }
}