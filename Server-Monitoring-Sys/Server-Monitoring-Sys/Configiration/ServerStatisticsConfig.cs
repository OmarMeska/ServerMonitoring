namespace ServerMonitor.Configuration;

public class ServerStatisticsConfig
{
    public const string SectionName = "ServerStatisticsConfig";

    public int SamplingIntervalSeconds { get; set; } = 60;
    public string ServerIdentifier { get; set; } = "DefaultServer";
}