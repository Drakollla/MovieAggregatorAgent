using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MovieAgentCLI.Plugins
{
    public class WebSearchPlugin
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        private const int MaxResults = 3;
        private const string SerperApiUrl = "https://google.serper.dev/search";

        public WebSearchPlugin(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;

            _apiKey = config["ApiKeys:Serper"] 
                ?? throw new ArgumentNullException("Serper API Key is missing");
        }

        [KernelFunction]
        [Description("Internet search. Use it to find the latest news, facts (like Oscar winners), and movie release dates.")]
        public async Task<string> SearchOnline([Description("Точный поисковый запрос")] string query)
        {
            try
            {
                string jsonResponse = await ExecuteSearchAsync(query);
                using var doc = JsonDocument.Parse(jsonResponse);

                return BuildSearchSummary(doc.RootElement);
            }
            catch (HttpRequestException httpEx)
            {
                return $"Ошибка API поиска: {httpEx.Message}";
            }
            catch (Exception ex)
            {
                return $"Непредвиденная ошибка при поиске: {ex.Message}";
            }
        }

        private async Task<string> ExecuteSearchAsync(string query)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SerperApiUrl);
            request.Headers.Add("X-API-KEY", _apiKey);
            request.Content = JsonContent.Create(new { q = query, gl = "ru", hl = "ru" });

            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Код ответа: {response.StatusCode}");

            return await response.Content.ReadAsStringAsync();
        }

        private static string BuildSearchSummary(JsonElement root)
        {
            var output = new StringBuilder("Результаты поиска:\n\n");

            bool hasAnswerBox = TryAppendAnswerBox(root, output);
            bool hasOrganic = TryAppendOrganicResults(root, output);

            return (hasAnswerBox || hasOrganic)
                ? output.ToString()
                : "Новостей и фактов по этому запросу не найдено.";
        }

        private static bool TryAppendAnswerBox(JsonElement root, StringBuilder output)
        {
            if (root.TryGetProperty("answerBox", out var answerBox) &&
                answerBox.TryGetProperty("snippet", out var snippet))
            {
                string answer = snippet.GetString() ?? "";

                if (!string.IsNullOrWhiteSpace(answer))
                {
                    output.AppendLine("--- Точный ответ (Google Answer Box) ---");
                    output.AppendLine($"{answer}\n");
                    return true;
                }
            }
            return false;
        }

        private static bool TryAppendOrganicResults(JsonElement root, StringBuilder output)
        {
            if (!root.TryGetProperty("organic", out var items) || items.GetArrayLength() == 0)
                return false;

            output.AppendLine("--- Результаты из сети ---");
            int limit = Math.Min(items.GetArrayLength(), MaxResults);

            for (int i = 0; i < limit; i++)
            {
                var item = items[i];
                string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "Без заголовка" : "Без заголовка";
                string snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";

                output.AppendLine($"- {title}\n  {snippet}\n");
            }
            return true;
        }
    }
}