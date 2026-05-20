using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MovieAgentCLI.Extensions;
using MovieAgentCLI.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    Log.Fatal((Exception)args.ExceptionObject, "Непредвиденная ошибка");
    Environment.Exit(1);
};

TaskScheduler.UnobservedTaskException += (sender, args) =>
{
    Log.Error(args.Exception, "Необработанная ошибка задачи");
    args.SetObserved();
};

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog();
    builder.Configuration.AddUserSecrets<Program>();
    builder.Services.AddApplicationServices(builder.Configuration);
    builder.Services.AddKernelWithProvider(builder.Configuration);
    builder.Services.AddHostedService<ConsoleChatService>();

    var host = builder.Build();
    await host.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
