using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;

namespace MovieAgentCLI.Plugins
{
    public class WebSearchPlugin
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public WebSearchPlugin(IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36");
            _apiKey = configuration["TavilyApiKey"] ?? "";
        }

        [KernelFunction]
        [Description("Поиск в реальном интернете для получения свежих новостей кино и дат выхода.")]
        public async Task<string> SearchOnline([Description("Запрос для поиска")] string query)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return "Ошибка: Ключ API не настроен.";

            try
            {
                var requestData = new
                {
                    api_key = _apiKey,
                    query = query,
                    search_depth = "basic",
                    max_results = 3
                };

                var response = await _httpClient.PostAsJsonAsync("https://api.tavily.com/search", requestData);

                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    return "Ошибка поиска: сервис отклонил запрос.";
                }

                var result = await response.Content.ReadFromJsonAsync<JsonElement>();

                if (result.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    var summary = "Свежая информация из интернета:\n";

                    foreach (var item in results.EnumerateArray())
                    {
                        string title = item.GetProperty("title").GetString() ?? "";
                        string content = item.GetProperty("content").GetString() ?? "";
                        summary += $"- {title}\n  Суть: {content}\n\n";
                    }

                    return summary;
                }

                return "Информации пока нет.";
            }
            catch (Exception ex)
            {
                return $"Ошибка сети: {ex.Message}";
            }
        }
    }
}