using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServerMonitor.Abstractions;
using ServerMonitor.Configuration;
using ServerMonitor.Messaging;
using ServerMonitor.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        // Bind config section
        services.Configure<ServerStatisticsConfig>(
            context.Configuration.GetSection(ServerStatisticsConfig.SectionName));

        // Register the message publisher — swap RabbitMqPublisher for any other IMessagePublisher
        services.AddSingleton<IMessagePublisher>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<RabbitMqPublisher>>();
            var rabbitHost = context.Configuration["RabbitMQ:Host"] ?? "localhost";
            return new RabbitMqPublisher(logger, rabbitHost);
        });

        // Register the hosted background service
        services.AddHostedService<ServerStatisticsService>();
    })
    .Build();

await host.RunAsync();