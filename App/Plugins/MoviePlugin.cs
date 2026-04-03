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
                string desc = mainMovie.TryGetProperty("description", out var d) ? d.GetString() : "Описание сюжета отсутствует.";

                var response = $"ГЛАВНЫЙ РЕЗУЛЬТАТ:\nФильм: {name} ({year})\nСюжет: {desc}\n";

                if (docs.GetArrayLength() > 1)
                {
                    response += "\nТАКЖЕ НАЙДЕНЫ:\n";
                    for (int i = 1; i < Math.Min(docs.GetArrayLength(), 5); i++)
                    {
                        var m = docs[i];
                        string altName = m.GetProperty("name").GetString() ?? "Без названия";
                        int altYear = m.TryGetProperty("year", out var ay) ? ay.GetInt32() : 0;
                        response += $"- {altName} ({altYear})\n";
                    }
                    response += "\nЕсли ты имел в виду один из этих фильмов, просто напиши его название.";
                }

                return response;
            }

            return "К сожалению, ничего не нашлось по вашему запросу.";
        }
    }
}