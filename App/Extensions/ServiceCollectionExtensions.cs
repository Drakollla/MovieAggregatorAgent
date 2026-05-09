using Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using MovieAgentCLI.Plugins;
using MovieAgentCLI.Services;
using MovieAgentCLI.Settings;
using Telegram.Bot;

namespace MovieAgentCLI.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpClient<IMovieService, KinopoiskService>();
            services.AddHttpClient<WebSearchPlugin>();

            string telegramToken = config["ApiKeys:Telegram"]
                ?? throw new InvalidOperationException("Токен Telegram не найден в конфигурации (ApiKeys:Telegram).");

            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramToken));
            services.AddHostedService<TelegramChatService>();

            //services.AddHostedService<ConsoleChatService>();

            return services;
        }

        public static IServiceCollection AddKernelWithOllama(this IServiceCollection services, IConfiguration config)
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
                   .AddFromType<CalendarPlugin>(); ;

            return services;
        }
    }
}