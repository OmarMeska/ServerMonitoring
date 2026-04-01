using AnomalyDetectionService.Abstractions;
using AnomalyDetectionService.Configuration;
using AnomalyDetectionService.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnomalyDetectionService.Services;

public sealed class AnomalyDetectionWorker : BackgroundService
{
    private readonly IMessageConsumer _consumer;
    private readonly IStatisticsRepository _repository;
    private readonly AlertService _alertService;
    private readonly AnomalyDetectionConfig _config;
    private readonly ILogger<AnomalyDetectionWorker> _logger;

    public AnomalyDetectionWorker(
        IMessageConsumer consumer,
        IStatisticsRepository repository,
        AlertService alertService,
        IOptions<AnomalyDetectionConfig> config,
        ILogger<AnomalyDetectionWorker> logger)
    {
        _consumer = consumer;
        _repository = repository;
        _alertService = alertService;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AnomalyDetectionWorker started.");

        await _consumer.StartConsumingAsync<ServerStatistics>(
            topicPattern: "ServerStatistics.*",
            onMessage: ProcessMessageAsync,
            cancellationToken: stoppingToken);

        // Keep alive until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(
        string serverIdentifier,
        ServerStatistics current,
        CancellationToken cancellationToken)
    {
        // Stamp the server identifier from the routing key
        current.ServerIdentifier = serverIdentifier;

        // 1. Fetch previous record for comparison
        var previous = await _repository.GetLatestAsync(serverIdentifier, cancellationToken);

        // 2. Persist current record
        await _repository.SaveAsync(current, cancellationToken);

        // 3. Run anomaly and high usage checks
        await RunChecksAsync(previous, current, cancellationToken);
    }

    private async Task RunChecksAsync(
        ServerStatistics? previous,
        ServerStatistics current,
        CancellationToken cancellationToken)
    {
        // --- Anomaly Alerts (require a previous sample to compare) ---
        if (previous is not null)
        {
            // Memory Usage Anomaly
            // if (CurrentMemoryUsage > PreviousMemoryUsage * (1 + MemoryUsageAnomalyThresholdPercentage))
            if (current.MemoryUsage > previous.MemoryUsage *
                (1 + _config.MemoryUsageAnomalyThresholdPercentage))
            {
                await _alertService.SendAnomalyAlertAsync(
                    serverIdentifier: current.ServerIdentifier,
                    metric: "MemoryUsage",
                    previousValue: previous.MemoryUsage,
                    currentValue: current.MemoryUsage,
                    cancellationToken: cancellationToken);
            }

            // CPU Usage Anomaly
            // if (CurrentCpuUsage > PreviousCpuUsage * (1 + CpuUsageAnomalyThresholdPercentage))
            if (previous.CpuUsage > 0 &&
                current.CpuUsage > previous.CpuUsage *
                (1 + _config.CpuUsageAnomalyThresholdPercentage))
            {
                await _alertService.SendAnomalyAlertAsync(
                    serverIdentifier: current.ServerIdentifier,
                    metric: "CpuUsage",
                    previousValue: previous.CpuUsage,
                    currentValue: current.CpuUsage,
                    cancellationToken: cancellationToken);
            }
        }

        // --- High Usage Alerts ---
        // Memory High Usage
        // if ((CurrentMemoryUsage / (CurrentMemoryUsage + CurrentAvailableMemory)) > MemoryUsageThresholdPercentage)
        var totalMemory = current.MemoryUsage + current.AvailableMemory;

        if (totalMemory > 0)
        {
            var memoryUsagePercentage = current.MemoryUsage / totalMemory;

            if (memoryUsagePercentage > _config.MemoryUsageThresholdPercentage)
            {
                await _alertService.SendHighUsageAlertAsync(
                    serverIdentifier: current.ServerIdentifier,
                    metric: "MemoryUsage",
                    usagePercentage: memoryUsagePercentage,
                    threshold: _config.MemoryUsageThresholdPercentage,
                    cancellationToken: cancellationToken);
            }
        }

        // CPU High Usage
        // if (CurrentCpuUsage > CpuUsageThresholdPercentage)
        if (current.CpuUsage > _config.CpuUsageThresholdPercentage)
        {
            await _alertService.SendHighUsageAlertAsync(
                serverIdentifier: current.ServerIdentifier,
                metric: "CpuUsage",
                usagePercentage: current.CpuUsage,
                threshold: _config.CpuUsageThresholdPercentage,
                cancellationToken: cancellationToken);
        }
    }
}