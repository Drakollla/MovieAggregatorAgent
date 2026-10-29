using Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using MovieAgentCLI.Handlers;
using MovieAgentCLI.LLM;
using MovieAgentCLI.Plugins;
using MovieAgentCLI.Services;
using Polly;
using Polly.Extensions.Http;
using Telegram.Bot;

namespace MovieAgentCLI.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddMemoryCache();
            services.AddTransient<RateLimitingHandler>();
            services.AddHttpClient<IMovieService, KinopoiskService>()
                .AddPolicyHandler(GetKinopoiskRetryPolicy())
                .AddHttpMessageHandler<RateLimitingHandler>();
            services.AddHttpClient<WebSearchPlugin>();

            string telegramToken = config["ApiKeys:Telegram"]
                ?? throw new InvalidOperationException("Токен Telegram не найден в конфигурации (ApiKeys:Telegram).");

            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramToken));
            services.AddHostedService<TelegramChatService>();

            return services;
        }

        private static IAsyncPolicy<HttpResponseMessage> GetKinopoiskRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        public static IServiceCollection AddSemanticKernelAgent(this IServiceCollection services, IConfiguration config)
        {
            var kernelBuilder = services.AddKernel();

            string providerName = config["LLM:Provider"] ?? "Ollama";
            
            ILLMProvider provider = providerName switch
            {
                "Ollama" => new OllamaProvider(),
                "Mock" => new MockProvider(),
                _ => throw new InvalidOperationException($"Неизвестный провайдер: {providerName}")
            };

            provider.Register(kernelBuilder, config);

            kernelBuilder.Plugins
                .AddFromType<MoviePlugin>()
                .AddFromType<WebSearchPlugin>()
                .AddFromType<CalendarPlugin>();

            return services;
        }
    }
}