using Contracts;
using Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MovieAgentCLI.Services;

namespace MovieAgentCLI.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddHttpClient<IMovieService, TmdbMovieService>();
            services.AddHostedService<ConsoleChatService>();

            return services;
        }
    }
}
