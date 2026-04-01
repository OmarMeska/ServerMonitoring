using AnomalyDetectionService.Abstractions;
using AnomalyDetectionService.Configuration;
using AnomalyDetectionService.Hubs;
using AnomalyDetectionService.Messaging;
using AnomalyDetectionService.Persistence;
using AnomalyDetectionService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Config
builder.Services.Configure<AnomalyDetectionConfig>(
    builder.Configuration.GetSection(AnomalyDetectionConfig.SectionName));

// SignalR
builder.Services.AddSignalR();

// Abstractions
builder.Services.AddSingleton<IStatisticsRepository, MongoStatisticsRepository>();
builder.Services.AddSingleton<IMessageConsumer>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<RabbitMqConsumer>>();
    var host = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    return new RabbitMqConsumer(logger, host);
});

// Services
builder.Services.AddSingleton<AlertService>();
builder.Services.AddHostedService<AnomalyDetectionWorker>();

var app = builder.Build();

app.UseStaticFiles();
// Map SignalR hub endpoint
app.MapHub<MonitoringHub>("/hubs/monitoring");

await app.RunAsync();