using Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MovieAgentCLI.Plugins
{
    public class CalendarPlugin
    {
        public const int DefaultMovieDurationMinutes = 120;
        public const int CalendarBufferMinutes = 10;
        private const string KinopoiskBaseUrl = "https://www.kinopoisk.ru/film/";

        private readonly IMovieService _movieService;
        private readonly ILogger<CalendarPlugin> _logger;

        public CalendarPlugin(IMovieService movieService, ILogger<CalendarPlugin> logger)
        {
            _movieService = movieService;
            _logger = logger;
        }

        [KernelFunction]
        [Description("Generate a Google Calendar link for a movie premiere or a planned movie night. Use this when the user asks to 'add to calendar', 'plan', or 'remind me'.")]
        public async Task<string> CreateEventLink(
            [Description("Movie title")] string movieTitle,
            [Description("Event date in YYYY-MM-DD format (e.g., 2025-01-01)")] string date,
            [Description("Optional. Event time in HH:mm format (e.g., 20:00). Leave empty for all-day events (premieres).")] string? time = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!DateTime.TryParse(date, out DateTime eventDate))
                    return "Error: Invalid date format. Use YYYY-MM-DD.";

                var movieDetails = await GetMovieDetailsAsync(movieTitle, cancellationToken);
                string datesParam;

                if (!string.IsNullOrWhiteSpace(time) && TimeSpan.TryParse(time, out TimeSpan eventTime))
                    datesParam = GetTimedEventDates(eventDate, eventTime, movieDetails.Duration);
                else datesParam = GetAllDayEventDates(eventDate);

                string calendarUrl = BuildGoogleCalendarUrl(movieTitle, datesParam, movieDetails.Url);

                return $"SYSTEM: Success! The Calendar URL is generated.\nCRITICAL INSTRUCTION: You MUST output this exact Markdown link in your final response: [Добавить в Google Календарь]({calendarUrl})\nDo not hide or modify the link!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating calendar link.");
                return $"Error generating link: {ex.Message}";
            }
        }

        #region Helpers

        private async Task<(int Duration, string Url)> GetMovieDetailsAsync(string movieTitle, CancellationToken cancellationToken)
        {
            try
            {
                var movies = await _movieService.SearchMoviesAsync(movieTitle, cancellationToken);
                var movie = movies.FirstOrDefault();

                if (movie is not null)
                {
                    int durationMinutes = movie.DurationMinutes ?? DefaultMovieDurationMinutes;
                    string url = $"{KinopoiskBaseUrl}{movie.Id}/";
                    return (durationMinutes, url);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get movie details for calendar.");
            }

            return (DefaultMovieDurationMinutes, "");
        }

        private static string BuildGoogleCalendarUrl(string movieTitle, string datesParam, string movieUrl)
        {
            string title = Uri.EscapeDataString($"Смотреть: {movieTitle}");

            string detailsText = string.IsNullOrEmpty(movieUrl)
                ? $"Напоминание о фильме «{movieTitle}». Приятного просмотра!"
                : $"Напоминание о фильме «{movieTitle}».\n\nСмотреть тут: {movieUrl}";

            string details = Uri.EscapeDataString(detailsText);

            return $"https://calendar.google.com/calendar/render?action=TEMPLATE&text={title}&dates={datesParam}&details={details}";
        }

        private static string GetTimedEventDates(DateTime eventDate, TimeSpan eventTime, int movieDurationMinutes)
        {
            int totalDuration = movieDurationMinutes + CalendarBufferMinutes;
            DateTime startDateTime = eventDate.Add(eventTime);
            DateTime endDateTime = startDateTime.AddMinutes(totalDuration);

            return $"{startDateTime:yyyyMMddTHHmmss}/{endDateTime:yyyyMMddTHHmmss}";
        }

        private static string GetAllDayEventDates(DateTime eventDate)
        {
            string startStr = eventDate.ToString("yyyyMMdd");
            string endStr = eventDate.AddDays(1).ToString("yyyyMMdd");

            return $"{startStr}/{endStr}";
        }

        #endregion
    }
}
