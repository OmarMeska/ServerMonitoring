namespace ServerMonitor.Abstractions;

public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a serializable payload to the given topic/routing key.
    /// </summary>
    Task PublishAsync<T>(string topic, T payload, CancellationToken cancellationToken = default);
}