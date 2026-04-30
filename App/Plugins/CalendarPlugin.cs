using Contracts;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace MovieAgentCLI.Plugins
{
    public class CalendarPlugin
    {
        public const int DefaultMovieDurationMinutes = 120;
        public const int CalendarBufferMinutes = 10;

        private readonly IMovieService _movieService;

        public CalendarPlugin(IMovieService movieService)
        {
            _movieService = movieService;
        }

        [KernelFunction]
        [Description("Generate a Google Calendar link for a movie premiere or a planned movie night. Use this when the user asks to 'add to calendar', 'plan', or 'remind me'.")]
        public async Task<string> CreateEventLink(
            [Description("Movie title")] string movieTitle,
            [Description("Event date in YYYY-MM-DD format (e.g., 2025-01-01)")] string date,
            [Description("Optional. Event time in HH:mm format (e.g., 20:00). Leave empty for all-day events (premieres).")] string? time = null)
        {
            try
            {
                if (!DateTime.TryParse(date, out DateTime eventDate))
                    return "Error: Invalid date format. Use YYYY-MM-DD.";

                var movieDetails = await GetMovieDetailsAsync(movieTitle);
                string datesParam;

                if (!string.IsNullOrWhiteSpace(time) && TimeSpan.TryParse(time, out TimeSpan eventTime))
                    datesParam = GetTimedEventDates(eventDate, eventTime, movieDetails.Duration);
                else datesParam = GetAllDayEventDates(eventDate);

                string calendarUrl = BuildGoogleCalendarUrl(movieTitle, datesParam, movieDetails.Url);

                return $"SYSTEM: Success! The Calendar URL is generated.\nCRITICAL INSTRUCTION: You MUST output this exact Markdown link in your final response: [Добавить в Google Календарь]({calendarUrl})\nDo not hide or modify the link!";
            }
            catch (Exception ex)
            {
                return $"Error generating link: {ex.Message}";
            }
        }

        #region Helpers
        private async Task<(int Duration, string Url)> GetMovieDetailsAsync(string movieTitle)
        {
            int durationMinutes = DefaultMovieDurationMinutes;
            string url = "";

            try
            {
                string searchJson = await _movieService.SearchMovieAsync(movieTitle);
                using var doc = JsonDocument.Parse(searchJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("docs", out var docs) && docs.GetArrayLength() > 0)
                {
                    var firstMovie = docs[0];

                    if (firstMovie.TryGetProperty("movieLength", out var lengthProp) && lengthProp.ValueKind != JsonValueKind.Null)
                        durationMinutes = lengthProp.GetInt32();

                    if (firstMovie.TryGetProperty("id", out var idProp))
                        url = $"https://www.kinopoisk.ru/film/{idProp.GetInt32()}/";
                }
            }
            catch { }

            return (durationMinutes, url);
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