using System.Text.Json;
using RabbitMQ.Client;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Integration;

namespace RetailPOS.Infrastructure.Integration;

public sealed record RabbitMqDeliveryOptions
{
    public bool Enabled { get; init; }
    public string? ConnectionUri { get; init; }
    public string? Exchange { get; init; }
    public int PublishTimeoutSeconds { get; init; } = 20;

    public void Validate()
    {
        if (!Enabled || !Uri.TryCreate(ConnectionUri, UriKind.Absolute, out var uri)
            || uri.Scheme != "amqps" || string.IsNullOrWhiteSpace(uri.UserInfo)
            || string.IsNullOrWhiteSpace(Exchange) || PublishTimeoutSeconds is < 1 or > 120)
            throw new InvalidOperationException("Approved TLS RabbitMQ credentials/exchange/confirm timeout are required.");
    }
}

public sealed class RabbitMqConfirmedPublisher(RabbitMqDeliveryOptions options) : IConfirmedIntegrationPublisher
{
    public async Task PublishAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken)
    {
        options.Validate(); envelope.Validate();
        var factory = new ConnectionFactory { Uri = new Uri(options.ConnectionUri!), AutomaticRecoveryEnabled = false };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.PublishTimeoutSeconds));
        try
        {
            await using var connection = await factory.CreateConnectionAsync(timeout.Token);
            await using var channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                timeout.Token);
            await channel.ExchangeDeclareAsync(options.Exchange!, ExchangeType.Topic,
                durable: true, autoDelete: false, cancellationToken: timeout.Token);
            await PublishOnConfirmedChannelAsync(channel, options.Exchange!, envelope, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("RabbitMQ confirm/routing operation timed out.");
        }
    }

    public static ValueTask PublishOnConfirmedChannelAsync(IChannel channel, string exchange,
        IntegrationEnvelope envelope, CancellationToken cancellationToken) =>
        channel.BasicPublishAsync(exchange, envelope.MessageType, mandatory: true,
            new BasicProperties
            {
                Persistent = true, ContentType = "application/json", MessageId = envelope.MessageId.ToString(),
                CorrelationId = envelope.CorrelationId.ToString(), Type = envelope.MessageType
            }, JsonSerializer.SerializeToUtf8Bytes(envelope), cancellationToken);
}

public sealed class RabbitMqInboxDelivery(IntegrationInbox inbox, ILogger<RabbitMqInboxDelivery> logger)
{
    // The approved consumer topology uses durable quorum queues, bounded delivery-limit and a durable dead-letter target.
    // RabbitMQ ACK occurs only after the database boundary completes; callers retain the original envelope on replay.
    public async Task HandleAsync(IChannel channel, ulong deliveryTag, ReadOnlyMemory<byte> body,
        IIntegrationConsumer consumer, CancellationToken cancellationToken)
    {
        try
        {
            if (body.Length > 1_100_000) throw new IntegrationPoisonException("Integration body exceeds the limit.");
            var envelope = JsonSerializer.Deserialize<IntegrationEnvelope>(body.Span)
                ?? throw new IntegrationPoisonException("Integration envelope is absent.");
            await inbox.ConsumeAsync(envelope, consumer, cancellationToken);
            await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception) when (exception is IntegrationConflictException
            or IntegrationPoisonException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            logger.LogError("Integration delivery rejected ({FailureType}) for consumer {Consumer}; dead-letter requested.",
                exception.GetType().Name, consumer.Name);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or TimeoutException
            or Microsoft.EntityFrameworkCore.DbUpdateException or Npgsql.NpgsqlException)
        {
            logger.LogWarning("Integration delivery failed ({FailureType}) for consumer {Consumer}; retry requested.",
                exception.GetType().Name, consumer.Name);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }
}
