using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MovieAgentCLI.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>();
builder.Services.AddApplicationServices();

var host = builder.Build();

await host.RunAsync();