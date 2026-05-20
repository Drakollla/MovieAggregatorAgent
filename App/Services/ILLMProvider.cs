using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MovieAgentCLI.Services
{
    public interface ILLMProvider
    {
        string Name { get; }
        void Register(IServiceCollection services, IConfiguration config);
    }
}
