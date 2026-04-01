namespace ServerMonitor.Models;

public class ServerStatistics
{
    public double MemoryUsage { get; set; }      // in MB
    public double AvailableMemory { get; set; }  // in MB
    public double CpuUsage { get; set; }         // percentage 0–100
    public DateTime Timestamp { get; set; }
}