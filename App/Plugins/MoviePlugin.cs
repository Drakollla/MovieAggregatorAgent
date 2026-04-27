using Contracts;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace MovieAgentCLI.Plugins
{
    public class MoviePlugin
    {
        private readonly IMovieService _movieService;

        public MoviePlugin(IMovieService movieService)
        {
            _movieService = movieService;
        }

        [KernelFunction]
        [Description("Получить информацию о фильме из Кинопоиска. Передавай сюда только обычное название фильма на русском языке.")]
        public async Task<string> SearchMovie([Description("Простое название фильма, например: Зеленая миля")] string query)
        {
            if (query.Trim().StartsWith("{"))
            {
                try
                {
                    using var qDoc = JsonDocument.Parse(query);

                    if (qDoc.RootElement.TryGetProperty("value", out var val))
                        query = val.GetString() ?? query;
                    else if (qDoc.RootElement.TryGetProperty("query", out var q))
                        query = q.GetString() ?? query;
                }
                catch { }
            }

            string rawJson = await _movieService.SearchMovieAsync(query);
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("docs", out var docs) || docs.GetArrayLength() == 0)
                return "База данных: Фильм не найден.";

            string lowerQuery = query.ToLower().Trim();

            var m = docs.EnumerateArray()
                .OrderByDescending(x =>
                {
                    string n = x.TryGetProperty("name", out var nameProp) ? nameProp.GetString()?.ToLower() ?? "" : "";
                    return n == lowerQuery ? 1 : 0;
                })
                .ThenByDescending(x =>
                {
                    return x.TryGetProperty("votes", out var v) && v.TryGetProperty("kp", out var kp) ? kp.GetInt32() : 0;
                })
                .First();

            string name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "Без названия" : "Без названия";
            int year = m.TryGetProperty("year", out var y) ? y.GetInt32() : 0;
            double rating = m.TryGetProperty("rating", out var r) && r.TryGetProperty("kp", out var kp) ? Math.Round(kp.GetDouble(), 1) : 0;
            string desc = m.TryGetProperty("description", out var d) ? d.GetString() ?? "Нет описания." : "Нет описания.";
            int id = m.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;

            if (desc.Length > 500)
                desc = desc.Substring(0, 500) + "... (сюжет обрезан)";

            return $"ДАННЫЕ ИЗ БАЗЫ: Название: {name} ({year}). Рейтинг: {rating}. Сюжет: {desc}. Ссылка: https://www.kinopoisk.ru/film/{id}/";
        }

        [KernelFunction]
        [Description("Поиск фильмов по точным критериям. Обязательно используй этот метод, если пользователь просит подобрать фильм по жанрам, году или рейтингу.")]
        public async Task<string> SearchByCriteria(
            [Description("Жанры фильма на русском языке через запятую (например: фантастика, боевик, комедия). Извлекай ВСЕ запрошенные жанры!")] string? genre = null,
            [Description("Начальный год (например, 1990)")] int? yearFrom = null,
            [Description("Конечный год (например, 1999)")] int? yearTo = null,
            [Description("Минимальный рейтинг от 1 до 10 (например, 7.5)")] float? ratingMin = null,
            [Description("Ключевые слова для поиска по сюжету")] string? keyword = null)
        {
            if (string.IsNullOrEmpty(genre) && !yearFrom.HasValue && !yearTo.HasValue && !ratingMin.HasValue && string.IsNullOrEmpty(keyword))
                return "Ошибка: Укажите хотя бы один критерий поиска.";

            string rawJson = await _movieService.SearchByCriteriaAsync(genre, yearFrom, yearTo, ratingMin, keyword);

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("docs", out var docs) && docs.GetArrayLength() > 0)
            {
                var summary = new List<string>();

                var requiredGenres = string.IsNullOrEmpty(genre)
                    ? new List<string>()
                    : genre.Split(',').Select(g => g.Trim().ToLower()).ToList();

                foreach (var m in docs.EnumerateArray())
                {
                    if (requiredGenres.Count > 1)
                    {
                        var movieGenres = new List<string>();

                        if (m.TryGetProperty("genres", out var gProp))
                        {
                            foreach (var g in gProp.EnumerateArray())
                            {
                                if (g.TryGetProperty("name", out var n))
                                    movieGenres.Add(n.GetString()?.ToLower() ?? "");
                            }
                        }

                        bool hasAllGenres = requiredGenres.All(req => movieGenres.Contains(req));

                        if (!hasAllGenres)
                            continue;
                    }

                    string name = m.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "Без названия" : "Без названия";
                    int year = m.TryGetProperty("year", out var y) ? y.GetInt32() : 0;
                    float rating = m.TryGetProperty("rating", out var r) && r.TryGetProperty("kp", out var kp) ? (float)kp.GetDouble() : 0;
                    int kpId = m.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;

                    string desc = m.TryGetProperty("description", out var d) ? d.GetString() ?? "Нет описания" : "Нет описания";

                    if (desc.Length > 200)
                        desc = desc.Substring(0, 200) + "...";

                    summary.Add($@"<MOVIE>
                        Название: {name} ({year})
                        Рейтинг: {rating:F1}
                        Сюжет: {desc}
                        Где смотреть: https://www.kinopoisk.ru/film/{kpId}/
                        </MOVIE>");

                    if (summary.Count >= 3)
                        break;
                }

                if (summary.Count == 0)
                    return "По вашим критериям ничего не найдено в базе.";

                return "ДАННЫЕ ИЗ БАЗЫ:\n" + string.Join("\n\n", summary);
            }

            return "По вашим критериям ничего не найдено в базе.";
        }

        [KernelFunction]
        [Description("Ищет похожие фильмы на основе выбранного фильма. Используй, когда пользователь просит найти что-то похожее на конкретный фильм.")]
        public async Task<string> FindSimilar([Description("Название фильма")] string movieTitle)
        {
            try
            {
                string searchJson = await _movieService.SearchMovieAsync(movieTitle);
                using var searchDoc = JsonDocument.Parse(searchJson);
                var searchRoot = searchDoc.RootElement;

                if (searchRoot.TryGetProperty("error", out var errorElement))
                    return $"Ошибка: {errorElement.GetString()}";

                if (!searchRoot.TryGetProperty("docs", out var docs) || docs.GetArrayLength() == 0)
                    return $"Фильм с названием «{movieTitle}» не найден. Попробуйте уточнить название.";

                var sourceMovie = docs.EnumerateArray()
                    .OrderByDescending(m =>
                    {
                        if (m.TryGetProperty("votes", out var votes) && votes.TryGetProperty("kp", out var kpVotes))
                            return kpVotes.GetInt32();
                        return 0;
                    })
                    .First();

                int movieId = sourceMovie.GetProperty("id").GetInt32();
                string sourceName = sourceMovie.GetProperty("name").GetString() ?? movieTitle;
                int sourceYear = sourceMovie.TryGetProperty("year", out var y) ? y.GetInt32() : 0;

                string fullMovieJson = await _movieService.GetMovieByIdAsync(movieId);
                using var fullDoc = JsonDocument.Parse(fullMovieJson);
                var fullRoot = fullDoc.RootElement;

                if (!fullRoot.TryGetProperty("similarMovies", out var similarMovies) || similarMovies.GetArrayLength() == 0)
                    return $"К сожалению, для фильма «{sourceName}» ({sourceYear}) Кинопоиск не предоставляет список похожих.";

                var franchiseIds = new HashSet<int>();

                if (fullRoot.TryGetProperty("sequelsAndPrequels", out var sequels))
                {
                    foreach (var seq in sequels.EnumerateArray())
                    {
                        if (seq.TryGetProperty("id", out var seqId))
                            franchiseIds.Add(seqId.GetInt32());
                    }
                }

                string response = $"Рекомендации Кинопоиска! Фильмы в духе «{sourceName}» ({sourceYear}):\n\n";

                string baseName = sourceName.Split(':')[0].Trim().ToLower();
                int count = 0;

                var seenFranchises = new HashSet<string>();

                for (int i = 0; i < similarMovies.GetArrayLength() && count < 10; i++)
                {
                    var m = similarMovies[i];
                    int kpId = m.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                    string name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "Без названия" : "Без названия";
                    string lowerName = name.ToLower();

                    if (franchiseIds.Contains(kpId) || lowerName.Contains(baseName) || baseName.Contains(lowerName))
                        continue;

                    string recBaseName = lowerName.Split(':')[0].Trim();
                    int andIndex = recBaseName.IndexOf(" и ");

                    if (andIndex > 0) 
                        recBaseName = recBaseName.Substring(0, andIndex).Trim();

                    if (seenFranchises.Contains(recBaseName))
                        continue;

                    seenFranchises.Add(recBaseName);

                    string yearStr = m.TryGetProperty("year", out var yearProp) ? $" ({yearProp.GetInt32()})" : "";
                    string kpLink = kpId > 0 ? $"https://www.kinopoisk.ru/film/{kpId}/" : "";

                    response += $"{count + 1}. {name}{yearStr}\n";

                    if (!string.IsNullOrEmpty(kpLink))
                        response += $"   Где посмотреть: {kpLink}\n";

                    response += "\n";
                    count++;
                }

                if (count == 0)
                    return $"Для фильма «{sourceName}» не нашлось независимых похожих фильмов.";

                return response;
            }
            catch (Exception ex)
            {
                return $"Произошла ошибка при поиске похожих фильмов: {ex.Message}";
            }
        }

    }
}