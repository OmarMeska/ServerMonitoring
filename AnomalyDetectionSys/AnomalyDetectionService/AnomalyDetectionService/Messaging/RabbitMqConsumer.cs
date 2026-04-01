using System.Text;
using System.Text.Json;
using AnomalyDetectionService.Abstractions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AnomalyDetectionService.Messaging;

public sealed class RabbitMqConsumer : IMessageConsumer, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private const string ExchangeName = "server.monitoring";
    private const string QueueName = "server.statistics.queue";

    public RabbitMqConsumer(ILogger<RabbitMqConsumer> logger, string hostName = "localhost")
    {
        _logger = logger;

        var factory = new ConnectionFactory { HostName = hostName };
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare same exchange and queue as the publisher
        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
    }

    public Task StartConsumingAsync<T>(
        string topicPattern,
        Func<string, T, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken = default)
    {
        // Bind queue to exchange with the topic pattern e.g. "ServerStatistics.*"
        _channel.QueueBind(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: topicPattern);

        // One message at a time — don't overwhelm the processor
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += async (_, args) =>
        {
            var routingKey = args.RoutingKey;

            try
            {
                var json = Encoding.UTF8.GetString(args.Body.ToArray());
                var payload = JsonSerializer.Deserialize<T>(json);

                if (payload is null)
                {
                    _logger.LogWarning("Received null payload on topic '{Topic}'.", routingKey);
                    _channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                // Extract server identifier from routing key: "ServerStatistics.Server123"
                var serverIdentifier = routingKey.Contains('.')
                    ? routingKey[(routingKey.IndexOf('.') + 1)..]
                    : routingKey;

                await onMessage(serverIdentifier, payload, cancellationToken);

                _channel.BasicAck(args.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message on topic '{Topic}'.", routingKey);
                _channel.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
            }
        };

        _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

        _logger.LogInformation("Started consuming from queue '{Queue}' with pattern '{Pattern}'.",
            QueueName, topicPattern);

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