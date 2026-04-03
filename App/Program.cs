using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using MovieAgentCLI.Extensions;
using MovieAgentCLI.Plugins;
using MovieAgentCLI.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>();
builder.Services.AddApplicationServices(builder.Configuration);

var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromMinutes(5)
};

builder.Services.AddKernel()
    .AddOpenAIChatCompletion(
        modelId: "qwen2.5:3b",
        endpoint: new Uri("http://localhost:11434/v1"),
        apiKey: "ollama"
    )
    .Plugins.AddFromType<MoviePlugin>();

builder.Services.AddHostedService<ConsoleChatService>();

var host = builder.Build();

await host.RunAsync();