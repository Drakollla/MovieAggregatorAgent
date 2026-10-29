using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using MovieAgentCLI.Settings;

namespace MovieAgentCLI.LLM
{
    public class OllamaProvider : ILLMProvider
    {
        public string Name => "Ollama";

        public void Register(IKernelBuilder builder, IConfiguration config)
        {
            var ollamaSettings = config.GetSection("OllamaSettings").Get<OllamaSettings>()
                ?? throw new InvalidOperationException(
                    "Секция OllamaSettings не найдена. Создайте appsettings.json с Endpoint и ModelId.");

            string endpoint = ollamaSettings.Endpoint
                ?? throw new InvalidOperationException("OllamaSettings:Endpoint не задан.");
            string modelId = ollamaSettings.ModelId
                ?? throw new InvalidOperationException("OllamaSettings:ModelId не задан.");

            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(endpoint),
                Timeout = TimeSpan.FromMinutes(5)
            };

            builder.AddOpenAIChatCompletion(
                    modelId: modelId,
                    apiKey: "ollama",
                    httpClient: httpClient
                );
        }
    }
}
