using Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieAgentCLI.Plugins;
using MovieAgentCLI.Services;
using Telegram.Bot;

namespace MovieAgentCLI.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddMemoryCache();
            services.AddHttpClient<IMovieService, KinopoiskService>();
            services.AddHttpClient<WebSearchPlugin>();

            string telegramToken = config["ApiKeys:Telegram"]
                ?? throw new InvalidOperationException("Токен Telegram не найден в конфигурации (ApiKeys:Telegram).");

            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramToken));
            services.AddHostedService<TelegramChatService>();
            //services.AddHostedService<ConsoleChatService>();

            return services;
        }

        public static IServiceCollection AddKernelWithProvider(this IServiceCollection services, IConfiguration config)
        {
            string providerName = config["LLM:Provider"] ?? "Ollama";

            ILLMProvider provider = providerName switch
            {
                "Ollama" => new OllamaProvider(),
                "Mock" => new MockProvider(),
                _ => throw new InvalidOperationException($"Неизвестный LLM-провайдер: {providerName}. Доступные: Ollama, Mock.")
            };

            provider.Register(services, config);

            return services;
        }
    }
}