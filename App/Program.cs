using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MovieAgentCLI.Extensions;
using MovieAgentCLI.Services;

AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    Console.WriteLine("Произошла непредвиденная ошибка. Попробуйте перезапустить приложение.");
    Environment.Exit(1);
};

TaskScheduler.UnobservedTaskException += (sender, args) =>
{
    Console.WriteLine("Что-то пошло не так. Попробуйте ещё раз.");
    args.SetObserved();
};

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>();
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddKernelWithOllama(builder.Configuration);

builder.Services.AddHostedService<ConsoleChatService>();

var host = builder.Build();

await host.RunAsync();