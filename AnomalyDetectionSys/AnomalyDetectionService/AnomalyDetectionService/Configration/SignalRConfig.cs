namespace AnomalyDetectionService.Configuration;

public class SignalRConfig
{
    public const string SectionName = "SignalRConfig";
    public string SignalRUrl { get; set; } = string.Empty;
}