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
        [Description("Ищет информацию о фильмах. Возвращает описание лучшего совпадения и список альтернатив.")]
        public async Task<string> SearchMovie([Description("Название фильма или поисковый запрос")] string query)
        {
            if (int.TryParse(query, out _))
                return "Ошибка: Пожалуйста, введите название фильма текстом.";

            string rawJson = await _movieService.SearchMovieAsync(query);
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("docs", out var docs) && docs.GetArrayLength() > 0)
            {
                var mainMovie = docs[0];
                string name = mainMovie.GetProperty("name").GetString() ?? "Без названия";
                int year = mainMovie.TryGetProperty("year", out var y) ? y.GetInt32() : 0;
                int kpId = mainMovie.GetProperty("id").GetInt32();
                string kpLink = $"https://www.kinopoisk.ru/film/{kpId}/";
                string desc = mainMovie.TryGetProperty("description", out var d) ? d.GetString() : "Описание сюжета отсутствует.";

                var response = $"ГЛАВНЫЙ РЕЗУЛЬТАТ:\nФильм: {name} ({year})\nСюжет: {desc}\nГде посмотреть: {kpLink}\n";

                if (docs.GetArrayLength() > 1)
                {
                    response += "\nТАКЖЕ НАЙДЕНЫ:\n";
                    for (int i = 1; i < Math.Min(docs.GetArrayLength(), 5); i++)
                    {
                        var m = docs[i];
                        string altName = m.GetProperty("name").GetString() ?? "Без названия";
                        int altYear = m.TryGetProperty("year", out var ay) ? ay.GetInt32() : 0;
                        int altKpId = m.GetProperty("id").GetInt32();
                        string altKpLink = $"https://www.kinopoisk.ru/film/{altKpId}/";
                        response += $"- {altName} ({altYear}) — {altKpLink}\n";
                    }
                    response += "\nЕсли ты имел в виду один из этих фильмов, просто напиши его название.";
                }

                return response;
            }

            return "К сожалению, ничего не нашлось по вашему запросу.";
        }

        [KernelFunction]
        [Description("Поиск фильмов по критериям. Используй когда пользователь описывает фильм словами.")]
        public async Task<string> SearchByCriteria(
            [Description("Жанр: thriller, comedy, drama, horror и т.д.")] string? genre = null,
            [Description("Год от, например: 1990")] int? yearFrom = null,
            [Description("Год до, например: 1999")] int? yearTo = null,
            [Description("Минимальный рейтинг, например: 7.5")] float? ratingMin = null,
            [Description("Ключевые слова в описании")] string? keyword = null)
        {
            if (string.IsNullOrEmpty(genre) && 
                !yearFrom.HasValue && !yearTo.HasValue && 
                !ratingMin.HasValue && 
                string.IsNullOrEmpty(keyword))
                return "Укажите хотя бы один критерий поиска.";

            string rawJson = await _movieService.SearchByCriteriaAsync(
                genre, yearFrom, yearTo, ratingMin, keyword
            );

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("docs", out var docs) && docs.GetArrayLength() > 0)
            {
                var results = "Найденные фильмы:\n\n";

                for (int i = 0; i < Math.Min(docs.GetArrayLength(), 5); i++)
                {
                    var m = docs[i];
                    string name = m.GetProperty("name").GetString() ?? "Без названия";
                    int year = m.TryGetProperty("year", out var y) ? y.GetInt32() : 0;
                    string? desc = m.TryGetProperty("description", out var d) ? d.GetString() : null;
                    float rating = m.TryGetProperty("rating", out var r) ? r.GetSingle() : 0;
                    int kpId = m.GetProperty("id").GetInt32();
                    string kpLink = $"https://www.kinopoisk.ru/film/{kpId}/";

                    results += $"{i + 1}. {name} ({year}) — рейтинг: {rating:F1}\nГде посмотреть: {kpLink}\n";
                    
                    if (!string.IsNullOrEmpty(desc))
                        results += $"   {desc}\n";
                    
                    results += "\n";
                }

                return results;
            }

            return "По вашим критериям ничего не найдено.";
        }

    }
}