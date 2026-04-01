using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerMonitor.Abstractions;
using ServerMonitor.Configuration;
using ServerMonitor.Models;

namespace ServerMonitor.Services;

public sealed class ServerStatisticsService : BackgroundService
{
    private readonly IMessagePublisher _publisher;
    private readonly ServerStatisticsConfig _config;
    private readonly ILogger<ServerStatisticsService> _logger;

    // CPU counter is stateful — must be reused across samples
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _availableMemoryCounter;

    public ServerStatisticsService(
        IMessagePublisher publisher,
        IOptions<ServerStatisticsConfig> config,
        ILogger<ServerStatisticsService> logger)
    {
        _publisher = publisher;
        _config = config.Value;
        _logger = logger;

        _cpuCounter = new PerformanceCounter(
            categoryName: "Processor",
            counterName: "% Processor Time",
            instanceName: "_Total",
            readOnly: true);

        _availableMemoryCounter = new PerformanceCounter(
            categoryName: "Memory",
            counterName: "Available MBytes",
            readOnly: true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ServerStatisticsService started. Sampling every {Interval}s for server '{Server}'.",
            _config.SamplingIntervalSeconds,
            _config.ServerIdentifier);

        // Prime the CPU counter — first call always returns 0
        _ = _cpuCounter.NextValue();
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        var interval = TimeSpan.FromSeconds(_config.SamplingIntervalSeconds);
        var topic = $"ServerStatistics.{_config.ServerIdentifier}";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stats = CollectStatistics();

                _logger.LogDebug(
                    "Collected stats: CPU={Cpu:F1}%, MemUsed={Mem:F1}MB, MemFree={Free:F1}MB",
                    stats.CpuUsage, stats.MemoryUsage, stats.AvailableMemory);

                await _publisher.PublishAsync(topic, stats, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to collect or publish server statistics.");
            }

            await Task.Delay(interval, stoppingToken);
        }

        _logger.LogInformation("ServerStatisticsService stopped.");
    }

    private ServerStatistics CollectStatistics()
    {
        // Available memory in MB (directly from counter)
        double availableMemoryMb = _availableMemoryCounter.NextValue();

        // Total physical memory via GC (cross-platform, no P/Invoke required)
        double totalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0);

        double usedMemoryMb = totalMemoryMb - availableMemoryMb;

        double cpuUsage = _cpuCounter.NextValue();

        return new ServerStatistics
        {
            MemoryUsage = Math.Round(usedMemoryMb, 2),
            AvailableMemory = Math.Round(availableMemoryMb, 2),
            CpuUsage = Math.Round(cpuUsage, 2),
            Timestamp = DateTime.UtcNow
        };
    }

    public override void Dispose()
    {
        _cpuCounter.Dispose();
        _availableMemoryCounter.Dispose();
        base.Dispose();
    }
}