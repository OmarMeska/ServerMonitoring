using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using ServerMonitor.Abstractions;

namespace ServerMonitor.Messaging;

public sealed class RabbitMqPublisher : IMessagePublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private const string ExchangeName = "server.monitoring";

    public RabbitMqPublisher(ILogger<RabbitMqPublisher> logger, string hostName = "localhost")
    {
        _logger = logger;

        var factory = new ConnectionFactory { HostName = hostName };
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // 1. Declare the exchange
        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        // 2. Declare the queue
        _channel.QueueDeclare(
            queue: "server.statistics.queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // 3. Bind queue to exchange
        _channel.QueueBind(
            queue: "server.statistics.queue",
            exchange: ExchangeName,
            routingKey: "ServerStatistics.#");
    }

    public Task PublishAsync<T>(string topic, T payload, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _channel.BasicPublish(
            exchange: ExchangeName,
            routingKey: topic,
            basicProperties: properties,
            body: body);

        _logger.LogDebug("Published message to topic '{Topic}'.", topic);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
    }
}