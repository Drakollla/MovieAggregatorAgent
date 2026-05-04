using Contracts;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Infrastructure
{
    public class KinopoiskService : IMovieService
    {
        private const string KinopoiskBaseUrl = "https://api.kinopoisk.dev/";
        private const int FutureYearsOffset = 10;

        private readonly HttpClient _httpClient;
        private readonly Random _random;

        public KinopoiskService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(KinopoiskBaseUrl);
            _random = new Random();

            var token = config["ApiKeys:Tmdb"]
                ?? throw new ArgumentNullException("API Token not found");

            _httpClient.DefaultRequestHeaders.Add("X-API-Key", token);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
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
            string url = $"v1.4/movie?page={randomPage}&limit=20&{queryParams}&sortField=votes.kp&sortType=-1";

            return await GetAsync(url);
        }

        private async Task<string> GetAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException)
            {
                return CreateErrorJson("Failed to connect to Kinopoisk server. Check your internet connection.");
            }
            catch (TaskCanceledException)
            {
                return CreateErrorJson("Request timed out. Please try again.");
            }
            catch (Exception ex)
            {
                return CreateErrorJson($"An error occurred: {ex.Message}");
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