using Contracts;
using Domain;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace MovieAgentCLI.Plugins
{
    public class MoviePlugin
    {
        public const int MaxDescriptionLength = 250;
        public const int MaxResults = 3;
        private const string KinopoiskBaseUrl = "https://www.kinopoisk.ru/film/";

        private readonly IMovieService _movieService;

        public MoviePlugin(IMovieService movieService)
        {
            _movieService = movieService;
        }

        [KernelFunction]
        [Description("Get detailed information about a specific movie from Kinopoisk.")]
        public async Task<string> SearchMovie(
            [Description("Movie title")] string query,
            CancellationToken cancellationToken = default)
        {
            query = ExtractQueryFromJsonFallback(query);

            var movies = await _movieService.SearchMoviesAsync(query, cancellationToken);
            var movie = movies.FirstOrDefault();

            if (movie is null)
                return "Фильм не найден.";

            return $"Название: {movie.Title} ({movie.Year}). Рейтинг: {movie.Rating:F1}. Сюжет: {TruncateDescription(movie.Description)}. Ссылка: {KinopoiskBaseUrl}{movie.Id}/\n[MOVIE:{movie.Id}]";
        }

        [KernelFunction]
        [Description("Search for movies by exact criteria (genres, year, rating). Use this when the user asks for recommendations by genre or year.")]
        public async Task<string> SearchByCriteria(
            [Description("Comma-separated genres in Russian (e.g., 'фантастика, боевик, комедия').")] string? genre = null,
            [Description("Start year (e.g., 1990)")] int? yearFrom = null,
            [Description("End year (e.g., 1999)")] int? yearTo = null,
            [Description("Minimum rating from 1 to 10 (e.g., 7.5)")] float? ratingMin = null,
            [Description("Keywords for plot search")] string? keyword = null,
            CancellationToken cancellationToken = default)
        {
            var requiredGenres = ParseGenres(genre);

            if (requiredGenres.Count == 0 && !yearFrom.HasValue && !yearTo.HasValue &&
                !ratingMin.HasValue && string.IsNullOrEmpty(keyword))
            {
                return "Ошибка: Укажите хотя бы один критерий поиска.";
            }

            var criteria = new MovieSearchCriteria
            {
                Genres = requiredGenres,
                YearFrom = yearFrom,
                YearTo = yearTo,
                RatingMin = ratingMin,
                Keyword = keyword
            };

            var movies = await _movieService.SearchByCriteriaAsync(criteria, cancellationToken);

            if (movies.Count == 0)
                return "По вашим критериям ничего не найдено в базе.";

            return BuildCriteriaResponse(movies, requiredGenres);
        }

        [KernelFunction]
        [Description("Find movies similar to a specific movie. Use this when the user asks 'recommend something similar to...'")]
        public async Task<string> FindSimilar(
            [Description("Movie title")] string movieTitle,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var sourceMovies = await _movieService.SearchMoviesAsync(movieTitle, cancellationToken);
                var source = sourceMovies.FirstOrDefault();

                if (source is null)
                    return $"Фильм с названием «{movieTitle}» не найден. Попробуйте уточнить название.";

                var fullMovie = await _movieService.GetMovieAsync(source.Id, cancellationToken);

                if (fullMovie is null)
                    return $"Фильм с названием «{movieTitle}» не найден. Попробуйте уточнить название.";

                if (fullMovie.Genres.Count == 0)
                    return $"Не удалось определить жанр фильма «{fullMovie.Title}».";

                int yearFrom = fullMovie.Year > 0 ? fullMovie.Year - 5 : 0;
                int yearTo = fullMovie.Year > 0 ? fullMovie.Year + 5 : DateTime.Now.Year;

                var criteria = new MovieSearchCriteria
                {
                    Genres = fullMovie.Genres,
                    YearFrom = yearFrom,
                    YearTo = yearTo
                };

                var candidates = await _movieService.SearchByCriteriaAsync(criteria, cancellationToken);

                var selected = candidates
                    .Where(m => m.Id != fullMovie.Id)
                    .OrderBy(_ => Random.Shared.Next())
                    .Take(MaxResults)
                    .ToList();

                if (selected.Count == 0)
                    return $"Похожих фильмов на «{fullMovie.Title}» ({fullMovie.Year}) не найдено.";

                return BuildSimilarResponse(selected, fullMovie.Title, fullMovie.Year);
            }
            catch (MovieServiceException ex)
            {
                return $"Ошибка базы: {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Произошла ошибка при поиске похожих фильмов: {ex.Message}";
            }
        }

        [KernelFunction]
        [Description("Get detailed movie information by Kinopoisk ID.")]
        public async Task<string> GetMovieDetails(
            [Description("Kinopoisk movie ID")] int movieId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var movie = await _movieService.GetMovieAsync(movieId, cancellationToken);

                if (movie is null)
                    return "Фильм не найден.";

                string genres = movie.Genres.Count > 0
                    ? string.Join(", ", movie.Genres)
                    : "Неизвестно";

                return $" {movie.Title} ({movie.Year})\n⭐ Рейтинг: {movie.Rating:F1}\n Жанры: {genres}\n📝 Сюжет: {TruncateDescription(movie.Description)}\n🔗 {KinopoiskBaseUrl}{movie.Id}/\n[MOVIE:{movie.Id}]";
            }
            catch (MovieServiceException ex)
            {
                return $"Ошибка базы: {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Ошибка при получении информации о фильме: {ex.Message}";
            }
        }

        #region Helpers

        private static List<string> ParseGenres(string? genreInput)
        {
            if (string.IsNullOrWhiteSpace(genreInput))
                return [];

            return genreInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .Select(g => g.ToLowerInvariant())
                             .ToList();
        }

        private static bool HasAllGenres(Movie movie, List<string> requiredGenres)
        {
            if (requiredGenres.Count == 0)
                return true;

            var movieGenres = movie.Genres
                .Select(g => g.ToLowerInvariant())
                .ToHashSet();

            return requiredGenres.All(movieGenres.Contains);
        }

        private static string TruncateDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "Нет описания";

            return description.Length > MaxDescriptionLength
                ? description[..MaxDescriptionLength] + "... (сюжет обрезан)"
                : description;
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
            catch
            {
            }

            return query;
        }

        private static string BuildCriteriaResponse(IReadOnlyList<Movie> movies, List<string> requiredGenres)
        {
            var selected = movies
                .Where(m => HasAllGenres(m, requiredGenres))
                .OrderBy(_ => Random.Shared.Next())
                .Take(MaxResults)
                .ToList();

            var output = new StringBuilder();
            var movieIds = new List<int>();

            foreach (var movie in selected)
            {
                movieIds.Add(movie.Id);
                AppendFormattedMovie(output, movie);
            }

            var result = selected.Count > 0
                ? output.ToString()
                : "По вашим критериям (с учетом проверки жанров) ничего не найдено в базе.";

            if (movieIds.Count > 0)
                result += $"\n[MOVIES:{string.Join(",", movieIds)}]";

            return result;
        }

        private static void AppendFormattedMovie(StringBuilder output, Movie movie)
        {
            output.AppendLine($"Название: {movie.Title} ({movie.Year})");
            output.AppendLine($"Рейтинг: {movie.Rating:F1}");
            output.AppendLine($"Сюжет: {TruncateDescription(movie.Description)}");
            output.AppendLine($"Ссылка: {KinopoiskBaseUrl}{movie.Id}/");
            output.AppendLine();
        }

        private static string BuildSimilarResponse(IReadOnlyList<Movie> movies, string sourceName, int sourceYear)
        {
            var output = new StringBuilder($"Похожие фильмы на «{sourceName}» ({sourceYear}):\n\n");
            var movieIds = new List<int>();
            int count = 0;

            foreach (var movie in movies)
            {
                movieIds.Add(movie.Id);
                count++;
                output.AppendLine($"{count}. {movie.Title} ({movie.Year}) ⭐{movie.Rating:F1}");
                output.AppendLine($"   {TruncateDescription(movie.Description)}");
                output.AppendLine($"   🔗 {KinopoiskBaseUrl}{movie.Id}/");
                output.AppendLine();
            }

            var result = output.ToString();
            if (movieIds.Count > 0)
                result += $"\n[MOVIES:{string.Join(",", movieIds)}]";

            return result;
        }

        #endregion
    }
}
