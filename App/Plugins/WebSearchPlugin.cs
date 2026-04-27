using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace MovieAgentCLI.Plugins
{
    public class WebSearchPlugin
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public WebSearchPlugin(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _apiKey = config["ApiKeys:Serper"] ?? throw new ArgumentNullException("Serper API Key is missing");
        }

        [KernelFunction]
        [Description("Поиск в интернете. Используй для поиска свежих новостей, фактов (кто получил Оскар) и дат выхода фильмов.")]
        public async Task<string> SearchOnline([Description("Поисковый запрос")] string query)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/search");
                request.Headers.Add("X-API-KEY", _apiKey);

                var payload = new { q = query, gl = "ru", hl = "ru" };
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return "Ошибка поиска в интернете.";

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("organic", out var items) && items.GetArrayLength() > 0)
                {
                    var summary = "Результаты поиска в интернете:\n";

                    for (int i = 0; i < Math.Min(items.GetArrayLength(), 3); i++)
                    {
                        var item = items[i];
                        string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        string snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";

                        summary += $"- {title}\n  {snippet}\n\n";
                    }
                    return summary;
                }

                return "Новостей по этому запросу не найдено.";
            }
            catch (Exception ex)
            {
                return $"Ошибка сети: {ex.Message}";
            }
        }
    }
}