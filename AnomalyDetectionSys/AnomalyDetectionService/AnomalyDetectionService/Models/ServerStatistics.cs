using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AnomalyDetectionService.Models;

public class ServerStatistics
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string ServerIdentifier { get; set; } = string.Empty;
    public double MemoryUsage { get; set; }       // in MB
    public double AvailableMemory { get; set; }   // in MB
    public double CpuUsage { get; set; }          // percentage 0-100
    public DateTime Timestamp { get; set; }
}