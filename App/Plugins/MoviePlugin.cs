using Contracts;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace MovieAgentCLI.Plugins
{
    public class MoviePlugin
    {
        private const int MaxCriteriaResults = 5;
        private const int MaxSimilarResults = 10;
        private const int MaxDescriptionLength = 500;
        private const string KinopoiskBaseUrl = "https://www.kinopoisk.ru/film/";

        private readonly IMovieService _movieService;

        public MoviePlugin(IMovieService movieService)
        {
            _movieService = movieService;
        }

        [KernelFunction]
        [Description("Получить информацию о фильме из Кинопоиска.")]
        public async Task<string> SearchMovie([Description("Название фильма")] string query)
        {
            query = ExtractQueryFromJsonFallback(query);

            string json = await _movieService.SearchMovieAsync(query);
            using var doc = JsonDocument.Parse(json);
            var bestMatch = GetBestMovieMatch(doc.RootElement, query);

            if (!bestMatch.HasValue)
                return "Фильм не найден.";

            var (name, year, rating, description, kpId) = ExtractMovieInfo(bestMatch.Value);

            return $"Название: {name} ({year}). Рейтинг: {rating}. Сюжет: {description}. Ссылка: {KinopoiskBaseUrl}{kpId}/";
        }

        [KernelFunction]
        [Description("Поиск фильмов по точным критериям: по жанру, году или рейтингу.")]
        public async Task<string> SearchByCriteria(
                    [Description("Жанры фильма на русском языке через запятую (например: фантастика, боевик).")] string? genre = null,
                    [Description("Начальный год")] int? yearFrom = null,
                    [Description("Конечный год")] int? yearTo = null,
                    [Description("Минимальный рейтинг от 1 до 10")] float? ratingMin = null,
                    [Description("Ключевые слова для поиска по сюжету")] string? keyword = null)
        {
            if (string.IsNullOrEmpty(genre) && !yearFrom.HasValue &&
                !yearTo.HasValue && !ratingMin.HasValue &&
                string.IsNullOrEmpty(keyword))
            {
                return "Ошибка: Укажите хотя бы один критерий поиска.";
            }

            string json = await _movieService.SearchByCriteriaAsync(genre, yearFrom, yearTo, ratingMin, keyword);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("docs", out var docs) || docs.GetArrayLength() == 0)
                return "По вашим критериям ничего не найдено в базе.";

            return BuildCriteriaResponse(docs, ParseGenres(genre));
        }


        [KernelFunction]
        [Description("Найти похожие фильмы. Использовать, когда пользователь просит найти что-то похожее на конкретный фильм.")]
        public async Task<string> FindSimilar([Description("Название фильма")] string movieTitle)
        {
            try
            {
                var (sourceMovie, sourceId, sourceName, sourceYear) = await GetBaseMovieAsync(movieTitle);

                if (sourceMovie == null)
                    return $"Фильм с названием «{movieTitle}» не найден. Попробуйте уточнить название.";

                string fullMovieJson = await _movieService.GetMovieByIdAsync(sourceId);
                using var fullDoc = JsonDocument.Parse(fullMovieJson);
                var fullRoot = fullDoc.RootElement;

                if (!fullRoot.TryGetProperty("similarMovies", out var similarMovies) || similarMovies.GetArrayLength() == 0)
                    return $"К сожалению, для фильма «{sourceName}» ({sourceYear}) Кинопоиск не предоставляет список похожих.";

                return BuildSimilarResponse(similarMovies, fullRoot, sourceName, sourceYear);
            }
            catch (Exception ex)
            {
                return $"Произошла ошибка при поиске похожих фильмов: {ex.Message}";
            }
        }

        #region Helpers

        private async Task<(JsonElement? element, int id, string name, int year)> GetBaseMovieAsync(string title)
        {
            string searchJson = await _movieService.SearchMovieAsync(title);
            using var searchDoc = JsonDocument.Parse(searchJson);
            var match = GetBestMovieMatch(searchDoc.RootElement, title);

            if (!match.HasValue)
                return (null, 0, string.Empty, 0);

            var element = match.Value;
            var (name, year, _, _, kpId) = ExtractMovieInfo(element);

            return (element, kpId, name, year);
        }

        private static List<string> ParseGenres(string? genreInput)
        {
            if (string.IsNullOrWhiteSpace(genreInput))
                return [];

            return genreInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .Select(g => g.ToLower())
                             .ToList();
        }

        private static bool HasAllGenres(JsonElement movie, List<string> requiredGenres)
        {
            var movieGenres = movie.TryGetProperty("genres", out var gProp)
                ? gProp.EnumerateArray().Select(g => g.TryGetProperty("name", out var n) 
                    ? n.GetString()?.ToLower() ?? "" : "").ToList()
                : [];

            return requiredGenres.All(movieGenres.Contains);
        }

        private static HashSet<int> ExtractFranchiseIds(JsonElement fullRoot)
        {
            var ids = new HashSet<int>();

            if (fullRoot.TryGetProperty("sequelsAndPrequels", out var sequels))
            {
                foreach (var seq in sequels.EnumerateArray())
                {
                    if (seq.TryGetProperty("id", out var seqId))
                        ids.Add(seqId.GetInt32());
                }
            }

            return ids;
        }

        private static string GetBaseFranchiseName(string movieName)
        {
            string baseName = movieName.Split(':')[0].Trim().ToLower();
            int andIndex = baseName.IndexOf(" и ");

            return andIndex > 0 ? baseName[..andIndex].Trim() : baseName;
        }

        private static bool IsDuplicateOrFranchise(JsonElement similarMovie,
            HashSet<int> franchiseIds,
            string sourceBaseName,
            HashSet<string> seenFranchises,
            out string name, out int kpId)
        {
            kpId = similarMovie.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
            name = similarMovie.TryGetProperty("name", out var n) ?
                n.GetString() ?? "Без названия" : "Без названия";

            string lowerName = name.ToLower();

            if (franchiseIds.Contains(kpId) ||
                lowerName.Contains(sourceBaseName) ||
                sourceBaseName.Contains(lowerName))
                return true;

            string recBaseName = GetBaseFranchiseName(lowerName);

            if (!seenFranchises.Add(recBaseName))
                return true;

            return false;
        }

        private static JsonElement? GetBestMovieMatch(JsonElement root, string originalQuery)
        {
            if (root.TryGetProperty("error", out _) ||
                !root.TryGetProperty("docs", out var docs) ||
                docs.GetArrayLength() == 0)
                return null;

            string lowerQuery = originalQuery.ToLower().Trim();

            return docs.EnumerateArray()
                .OrderByDescending(x => (x.TryGetProperty("name", out var nameProp) ? nameProp.GetString()?.ToLower() ?? "" : "") == lowerQuery ? 1 : 0)
                .ThenByDescending(x => x.TryGetProperty("votes", out var v) && v.TryGetProperty("kp", out var kp) ? kp.GetInt32() : 0)
                .FirstOrDefault();
        }

        private static (string name, int year, float rating, string description, int kpId) ExtractMovieInfo(JsonElement element)
        {
            var name = element.TryGetProperty("name", out var n) ? n.GetString() ?? "Без названия" : "Без названия";
            var year = element.TryGetProperty("year", out var y) ? y.GetInt32() : 0;
            var rating = element.TryGetProperty("rating", out var r) && r.TryGetProperty("kp", out var kp) ? (float)kp.GetDouble() : 0;
            var description = element.TryGetProperty("description", out var d) ? d.GetString() ?? "Нет описания" : "Нет описания";
            var kpId = element.TryGetProperty("id", out var id) ? id.GetInt32() : 0;

            if (description.Length > MaxDescriptionLength)
                description = description[..MaxDescriptionLength] + "... (сюжет обрезан)";

            return (name, year, rating, description, kpId);
        }

        private static string ExtractQueryFromJsonFallback(string query)
        {
            if (!query.Trim().StartsWith("{"))
                return query;

            try
            {
                using var qDoc = JsonDocument.Parse(query);

                if (qDoc.RootElement.TryGetProperty("value", out var val))
                    return val.GetString() ?? query;

                if (qDoc.RootElement.TryGetProperty("query", out var q))
                    return q.GetString() ?? query;
            }
            catch { }

            return query;
        }

        private static string BuildCriteriaResponse(JsonElement docs, List<string> requiredGenres)
        {
            var output = new StringBuilder("Найденные фильмы:\n");
            int count = 0;

            foreach (var element in docs.EnumerateArray())
            {
                if (requiredGenres.Count > 0 && !HasAllGenres(element, requiredGenres))
                    continue;

                AppendFormattedMovie(output, element);

                if (++count >= MaxCriteriaResults)
                    break;
            }

            return count > 0 ? output.ToString() : "По вашим критериям (с учетом проверки жанров) ничего не найдено.";
        }

        private static void AppendFormattedMovie(StringBuilder output, JsonElement element)
        {
            var (name, year, rating, description, kpId) = ExtractMovieInfo(element);

            output.AppendLine("<MOVIE>");
            output.AppendLine($"Название: {name} ({year})");
            output.AppendLine($"Рейтинг: {rating:F1}");
            output.AppendLine($"Сюжет: {description}");
            output.AppendLine($"Где смотреть: {KinopoiskBaseUrl}{kpId}/");
            output.AppendLine("</MOVIE>\n");
        }

        private static string BuildSimilarResponse(JsonElement similarMovies, JsonElement fullRoot, string sourceName, int sourceYear)
        {
            var franchiseIds = ExtractFranchiseIds(fullRoot);
            string baseName = GetBaseFranchiseName(sourceName);
            var seenFranchises = new HashSet<string>();

            var output = new StringBuilder($"Рекомендации Кинопоиска! Фильмы в духе «{sourceName}» ({sourceYear}):\n\n");
            int count = 0;

            foreach (var movie in similarMovies.EnumerateArray())
            {
                if (IsDuplicateOrFranchise(movie, franchiseIds, baseName, seenFranchises, out string name, out int kpId))
                    continue;

                AppendFormattedSimilarMovie(output, movie, name, kpId, ref count);

                if (count >= MaxSimilarResults)
                    break;
            }

            return count > 0 ? output.ToString() : $"Для фильма «{sourceName}» не нашлось независимых похожих фильмов.";
        }

        private static void AppendFormattedSimilarMovie(StringBuilder output,
            JsonElement movie, string name,
            int kpId, ref int count)
        {
            count++;

            string yearStr = movie.TryGetProperty("year", out var yearProp) ?
                $" ({yearProp.GetInt32()})" : "";

            output.AppendLine($"{count}. {name}{yearStr}");

            if (kpId > 0)
                output.AppendLine($"   Где посмотреть: {KinopoiskBaseUrl}{kpId}/");

            output.AppendLine();
        }

        #endregion
    }
}