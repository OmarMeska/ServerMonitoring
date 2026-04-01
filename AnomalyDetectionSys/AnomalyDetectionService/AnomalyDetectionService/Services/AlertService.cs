using AnomalyDetectionService.Hubs;
using AnomalyDetectionService.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AnomalyDetectionService.Services;

public sealed class AlertService
{
    private readonly IHubContext<MonitoringHub> _hubContext;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IHubContext<MonitoringHub> hubContext,
        ILogger<AlertService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendAnomalyAlertAsync(
        string serverIdentifier,
        string metric,
        double previousValue,
        double currentValue,
        CancellationToken cancellationToken = default)
    {
        var alert = new
        {
            AlertType = "AnomalyAlert",
            ServerIdentifier = serverIdentifier,
            Metric = metric,
            PreviousValue = previousValue,
            CurrentValue = currentValue,
            Timestamp = DateTime.UtcNow,
            Message = $"Anomaly detected on {serverIdentifier}: {metric} jumped from " +
                      $"{previousValue:F2} to {currentValue:F2}"
        };

        _logger.LogWarning("Anomaly Alert — {Server} | {Metric}: {Previous} → {Current}",
            serverIdentifier, metric, previousValue, currentValue);

        await _hubContext.Clients.All.SendAsync("AnomalyAlert", alert, cancellationToken);
    }

    public async Task SendHighUsageAlertAsync(
        string serverIdentifier,
        string metric,
        double usagePercentage,
        double threshold,
        CancellationToken cancellationToken = default)
    {
        var alert = new
        {
            AlertType = "HighUsageAlert",
            ServerIdentifier = serverIdentifier,
            Metric = metric,
            UsagePercentage = usagePercentage,
            Threshold = threshold,
            Timestamp = DateTime.UtcNow,
            Message = $"High usage on {serverIdentifier}: {metric} at " +
                      $"{usagePercentage:P1}, threshold is {threshold:P1}"
        };

        _logger.LogWarning("High Usage Alert — {Server} | {Metric}: {Usage:P1} exceeds {Threshold:P1}",
            serverIdentifier, metric, usagePercentage, threshold);

        await _hubContext.Clients.All.SendAsync("HighUsageAlert", alert, cancellationToken);
    }
}