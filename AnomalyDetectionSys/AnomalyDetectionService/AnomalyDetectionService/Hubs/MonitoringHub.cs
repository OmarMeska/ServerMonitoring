using Microsoft.AspNetCore.SignalR;

namespace AnomalyDetectionService.Hubs;

public class MonitoringHub : Hub
{
    // Clients connect here to receive real-time alerts
    // Methods are invoked server-side via IHubContext<MonitoringHub>
}