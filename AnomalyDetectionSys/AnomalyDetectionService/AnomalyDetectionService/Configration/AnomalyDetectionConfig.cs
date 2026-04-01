namespace AnomalyDetectionService.Configuration;

public class AnomalyDetectionConfig
{
    public const string SectionName = "AnomalyDetectionConfig";

    // e.g. 0.4 means alert if usage jumps 40% above previous sample
    public double MemoryUsageAnomalyThresholdPercentage { get; set; } = 0.4;
    public double CpuUsageAnomalyThresholdPercentage { get; set; } = 0.5;

    // e.g. 0.8 means alert if memory usage exceeds 80% of total
    public double MemoryUsageThresholdPercentage { get; set; } = 0.8;
    public double CpuUsageThresholdPercentage { get; set; } = 0.9;
}