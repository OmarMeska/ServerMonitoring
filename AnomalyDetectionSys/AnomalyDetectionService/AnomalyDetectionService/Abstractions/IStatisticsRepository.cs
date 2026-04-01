using AnomalyDetectionService.Models;

namespace AnomalyDetectionService.Abstractions;

public interface IStatisticsRepository
{
    /// <summary>Persists a statistics record to the data store.</summary>
    Task SaveAsync(ServerStatistics statistics, CancellationToken cancellationToken = default);

    /// <summary>Retrieves the most recent record for a given server.</summary>
    Task<ServerStatistics?> GetLatestAsync(string serverIdentifier, CancellationToken cancellationToken = default);
}