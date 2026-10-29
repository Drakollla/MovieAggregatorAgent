using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace MovieAgentCLI.LLM
{
    public interface ILLMProvider
    {
        string Name { get; }
        void Register(IKernelBuilder builder, IConfiguration config);
    }
}
