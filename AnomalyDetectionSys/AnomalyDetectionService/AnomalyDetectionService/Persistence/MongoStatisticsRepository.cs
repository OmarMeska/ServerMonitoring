using AnomalyDetectionService.Abstractions;
using AnomalyDetectionService.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace AnomalyDetectionService.Persistence;

public sealed class MongoStatisticsRepository : IStatisticsRepository
{
    private readonly IMongoCollection<ServerStatistics> _collection;
    private readonly ILogger<MongoStatisticsRepository> _logger;

    public MongoStatisticsRepository(
        IConfiguration configuration,
        ILogger<MongoStatisticsRepository> logger)
    {
        _logger = logger;

        var connectionString = configuration["MongoDB:ConnectionString"] ?? "mongodb://localhost:27017";
        var databaseName = configuration["MongoDB:Database"] ?? "ServerMonitor";
        var collectionName = configuration["MongoDB:Collection"] ?? "ServerStatistics";

        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        _collection = database.GetCollection<ServerStatistics>(collectionName);

        // Index on ServerIdentifier + Timestamp for fast latest-record queries
        var indexKeys = Builders<ServerStatistics>.IndexKeys
            .Ascending(s => s.ServerIdentifier)
            .Descending(s => s.Timestamp);

        _collection.Indexes.CreateOne(new CreateIndexModel<ServerStatistics>(indexKeys));

        _logger.LogInformation("MongoDB repository initialized — {Database}/{Collection}",
            databaseName, collectionName);
    }

    public async Task SaveAsync(ServerStatistics statistics, CancellationToken cancellationToken = default)
    {
        await _collection.InsertOneAsync(statistics, cancellationToken: cancellationToken);
        _logger.LogDebug("Saved statistics for server '{Server}'.", statistics.ServerIdentifier);
    }

    public async Task<ServerStatistics?> GetLatestAsync(
        string serverIdentifier,
        CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(s => s.ServerIdentifier == serverIdentifier)
            .SortByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
    }
}