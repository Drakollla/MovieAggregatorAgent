using Contracts;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Infrastructure
{
    public class KinopoiskService : IMovieService
    {
        private readonly HttpClient _httpClient;

        public KinopoiskService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri("https://api.kinopoisk.dev/");

            var token = config["ApiKeys:Tmdb"]
                        ?? throw new ArgumentNullException("API Token not found");

            _httpClient.DefaultRequestHeaders.Add("X-API-Key", token);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        public async Task<string> SearchMovieAsync(string query)
        {
            var url = $"v1.4/movie/search?page=1&limit=5&query={Uri.EscapeDataString(query)}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException ex)
            {
                return JsonSerializer.Serialize(new { error = "Failed to connect to Kinopoisk server. Check your internet connection." });
            }
            catch (TaskCanceledException)
            {
                return JsonSerializer.Serialize(new { error = "Request timed out. Please try again." });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = "An error occurred while searching for movies." });
            }
        }

        public async Task<string> SearchByCriteriaAsync(string? genre = null,
            int? yearFrom = null,
            int? yearTo = null,
            float? ratingMin = null,
            string? keyword = null)
        {
            var queryParams = new List<string>();

            if (yearFrom.HasValue && yearTo.HasValue)
                queryParams.Add($"year={yearFrom}-{yearTo}");
            else if (yearFrom.HasValue)
                queryParams.Add($"year={yearFrom}-2030");
            else if (yearTo.HasValue)
                queryParams.Add($"year=1890-{yearTo}");

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

            var url = $"v1.4/movie?page=1&limit=20&{string.Join("&", queryParams)}&sortField=votes.kp&sortType=-1";

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> GetMovieByIdAsync(int id)
        {
            var url = $"v1.4/movie/{id}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException)
            {
                return JsonSerializer.Serialize(new { error = "Failed to connect to Kinopoisk server." });
            }
            catch (TaskCanceledException)
            {
                return JsonSerializer.Serialize(new { error = "Request timed out." });
            }
            catch (Exception)
            {
                return JsonSerializer.Serialize(new { error = "An error occurred." });
            }
        }
    }
}