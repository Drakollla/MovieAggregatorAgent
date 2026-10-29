using Contracts;
using Domain;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly IMemoryCache _cache;

        public KinopoiskService(HttpClient httpClient, IConfiguration config, ILogger<KinopoiskService> logger, IMemoryCache cache)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(KinopoiskBaseUrl);
            _logger = logger;
            _cache = cache;

            var token = config["ApiKeys:Tmdb"]
                ?? throw new ArgumentNullException("API Token not found");

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", token);
        }

        public async Task<IReadOnlyList<Movie>> SearchMoviesAsync(string query, CancellationToken cancellationToken = default)
        {
            string url = $"v1.4/movie/search?page=1&limit=5&query={Uri.EscapeDataString(query)}";
            string json = await GetAsync(url, TimeSpan.FromMinutes(30), cancellationToken);

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("docs", out var docs) || docs.ValueKind != JsonValueKind.Array)
                return [];

            return docs.EnumerateArray()
                .OrderByDescending(movie => IsExactTitleMatch(movie, query) ? 1 : 0)
                .ThenByDescending(GetVotes)
                .Select(MapMovie)
                .ToList();
        }

        public async Task<Movie?> GetMovieAsync(int id, CancellationToken cancellationToken = default)
        {
            string json = await GetAsync($"v1.4/movie/{id}", TimeSpan.FromHours(24), cancellationToken);

            using var doc = JsonDocument.Parse(json);
            return MapMovie(doc.RootElement);
        }

        public async Task<IReadOnlyList<Movie>> SearchByCriteriaAsync(MovieSearchCriteria criteria, CancellationToken cancellationToken = default)
        {
            string queryParams = BuildCriteriaQuery(criteria);
            int randomPage = Random.Shared.Next(1, 4);
            string url = $"v1.4/movie?page={randomPage}&limit=10&{queryParams}&sortField=votes.kp&sortType=-1";

            string json = await GetAsync(url, cancellationToken: cancellationToken);

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("docs", out var docs) || docs.ValueKind != JsonValueKind.Array)
                return [];

            return docs.EnumerateArray().Select(MapMovie).ToList();
        }

        private async Task<string> GetAsync(string url, TimeSpan? cacheDuration = null, CancellationToken cancellationToken = default)
        {
            string? cacheKey = cacheDuration.HasValue ? url : null;

            if (cacheKey is not null && _cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            {
                _logger.LogDebug("Cache hit for {Url}", url);
                return cached;
            }

            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Kinopoisk API error ({StatusCode}): {Error}", response.StatusCode, errorContent);
                    throw new MovieServiceException($"Kinopoisk API вернул {(int)response.StatusCode}.");
                }

                string result = await response.Content.ReadAsStringAsync(cancellationToken);

                if (cacheKey is not null)
                {
                    _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = cacheDuration!.Value
                    });
                    _logger.LogDebug("Cached response for {Url}", url);
                }

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Kinopoisk request timed out");
                throw new MovieServiceException("Превышено время ожидания. Попробуйте ещё раз.", ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Kinopoisk HTTP request failed");
                throw new MovieServiceException("Не удалось подключиться к серверу Кинопоиска. Проверьте интернет-соединение.", ex);
            }
            catch (MovieServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Kinopoisk service");
                throw new MovieServiceException($"Произошла ошибка: {ex.Message}", ex);
            }
        }

        private static string BuildCriteriaQuery(MovieSearchCriteria criteria)
        {
            var queryParams = new List<string>();

            if (criteria.YearFrom.HasValue && criteria.YearTo.HasValue)
            {
                queryParams.Add($"year={criteria.YearFrom}-{criteria.YearTo}");
            }
            else if (criteria.YearFrom.HasValue)
            {
                int futureYear = DateTime.Now.Year + FutureYearsOffset;
                queryParams.Add($"year={criteria.YearFrom}-{futureYear}");
            }
            else if (criteria.YearTo.HasValue)
            {
                queryParams.Add($"year=1890-{criteria.YearTo}");
            }

            if (criteria.RatingMin.HasValue)
            {
                string ratingStr = criteria.RatingMin.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                queryParams.Add($"rating.kp={ratingStr}-10");
            }

            foreach (string genre in criteria.Genres)
            {
                queryParams.Add($"genres.name={Uri.EscapeDataString(genre.ToLowerInvariant())}");
            }

            // Kinopoisk's movie list endpoint has no free-text filter; Keyword is currently unused.
            return string.Join("&", queryParams);
        }

        private static Movie MapMovie(JsonElement element)
        {
            int id = GetInt32(element, "id");
            string title = GetString(element, "name") ?? "Без названия";
            int year = GetInt32(element, "year");

            float rating = element.TryGetProperty("rating", out var ratingElement)
                && ratingElement.ValueKind == JsonValueKind.Object
                && ratingElement.TryGetProperty("kp", out var kp)
                && kp.ValueKind == JsonValueKind.Number
                    ? (float)kp.GetDouble()
                    : 0;

            string? description = GetString(element, "description");

            string? posterUrl = null;
            if (element.TryGetProperty("poster", out var poster) && poster.ValueKind == JsonValueKind.Object)
                posterUrl = GetString(poster, "url") ?? GetString(poster, "previewUrl");

            int? durationMinutes = null;
            if (element.TryGetProperty("movieLength", out var length) && length.ValueKind == JsonValueKind.Number)
                durationMinutes = length.GetInt32();

            var genres = new List<string>();
            if (element.TryGetProperty("genres", out var genresElement) && genresElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var genre in genresElement.EnumerateArray())
                {
                    string? name = GetString(genre, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                        genres.Add(name);
                }
            }

            return new Movie(id, title, year, rating, description, posterUrl, durationMinutes, genres);
        }

        private static bool IsExactTitleMatch(JsonElement element, string query)
        {
            string? name = GetString(element, "name");
            return string.Equals(name?.Trim(), query.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static int GetVotes(JsonElement element)
        {
            if (element.TryGetProperty("votes", out var votes)
                && votes.ValueKind == JsonValueKind.Object
                && votes.TryGetProperty("kp", out var kp)
                && kp.ValueKind == JsonValueKind.Number)
            {
                return kp.GetInt32();
            }

            return 0;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static int GetInt32(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : 0;
        }
    }
}
