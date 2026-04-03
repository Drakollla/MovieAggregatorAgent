using Contracts;
using Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using MovieAgentCLI.Services;

namespace MovieAgentCLI.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpClient<IMovieService, KinopoiskService>();

            var ollamaEndpoint = config["OllamaSettings:Endpoint"];
            var ollamaModel = config["OllamaSettings:ModelId"];
            var ollamaApiKey = config["ApiKeys:Ollama"];

            var endpoint = config["OllamaSettings:Endpoint"];
            var modelId = config["OllamaSettings:ModelId"];
            var apiKey = config["ApiKeys:Ollama"];

            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(endpoint),
                Timeout = TimeSpan.FromMinutes(5)
            };

            services.AddKernel()
                .AddOpenAIChatCompletion(
                    modelId: modelId,
                    apiKey: apiKey,
                    httpClient: httpClient
                );

            services.AddHostedService<ConsoleChatService>();

            return services;
        }
    }
}
