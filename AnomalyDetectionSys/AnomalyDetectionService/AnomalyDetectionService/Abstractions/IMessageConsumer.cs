namespace AnomalyDetectionService.Abstractions;

public interface IMessageConsumer
{
    /// <summary>
    /// Subscribes to the given topic pattern and invokes the handler for each message received.
    /// </summary>
    Task StartConsumingAsync<T>(
        string topicPattern,
        Func<string, T, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken = default);
}