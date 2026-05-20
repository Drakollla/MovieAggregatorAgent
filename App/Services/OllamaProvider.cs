using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using MovieAgentCLI.Plugins;
using MovieAgentCLI.Settings;

namespace MovieAgentCLI.Services
{
    public class OllamaProvider : ILLMProvider
    {
        public string Name => "Ollama";

        public void Register(IServiceCollection services, IConfiguration config)
        {
            var ollamaSettings = config.GetSection("OllamaSettings").Get<OllamaSettings>() ?? new OllamaSettings();
            var apiKey = config["ApiKeys:Ollama"] ?? "ollama";

            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(ollamaSettings.Endpoint),
                Timeout = TimeSpan.FromMinutes(5)
            };

            services.AddKernel()
                .AddOpenAIChatCompletion(
                    modelId: ollamaSettings.ModelId,
                    apiKey: apiKey,
                    httpClient: httpClient
                )
                .Plugins
                    .AddFromType<MoviePlugin>()
                    .AddFromType<WebSearchPlugin>()
                    .AddFromType<CalendarPlugin>();
        }
    }
}
