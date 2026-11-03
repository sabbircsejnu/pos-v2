using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Infrastructure.StockCounts;

namespace RetailPOS.Infrastructure.Integration;

/// <summary>
/// Handles one RabbitMQ delivery of a Stock Count adjustment request. The message is acknowledged only after
/// the processor finished (the PostgreSQL job already holds the durable outcome); poison messages are
/// dead-lettered and every other failure is left to the PostgreSQL-driven sweeper, so a delivery can never
/// become an infinite loop or a lost job. Duplicate or redelivered messages are idempotent no-ops.
/// </summary>
public sealed class StockCountAdjustmentDelivery(
    IStockCountAdjustmentProcessor processor,
    ILogger<StockCountAdjustmentDelivery> logger)
{
    public const string QueueName = "retailpos.stock-count.adjustment";

    public async Task HandleAsync(
        IChannel channel,
        ulong deliveryTag,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        try
        {
            if (body.Length > 1_100_000)
                throw new IntegrationPoisonException("Integration body exceeds the limit.");
            var envelope = JsonSerializer.Deserialize<IntegrationEnvelope>(body.Span)
                ?? throw new IntegrationPoisonException("Integration envelope is absent.");
            envelope.Validate();
            var payload = StockCountAdjustmentMessage.Deserialize(envelope.PayloadJson);
            if (envelope.Producer != StockCountAdjustmentMessage.Producer
                || envelope.MessageType != StockCountAdjustmentMessage.MessageType
                || envelope.SchemaVersion != StockCountAdjustmentMessage.SchemaVersion
                || envelope.AggregateType != StockCountAdjustmentMessage.AggregateType
                || payload is null
                || payload.BusinessId != envelope.BusinessId
                || payload.JobId == Guid.Empty
                || envelope.AggregateId != payload.JobId.ToString())
            {
                throw new IntegrationPoisonException("Unsupported Stock Count adjustment message.");
            }

            var outcome = await processor.ProcessAsync(payload.JobId, envelope.BusinessId, cancellationToken);
            logger.LogInformation(
                "Stock Count adjustment job {JobId} delivery outcome {Outcome}.",
                payload.JobId,
                outcome);
            await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception) when (exception is IntegrationPoisonException
            or InvalidDataException or JsonException)
        {
            logger.LogError(
                "Stock Count adjustment delivery rejected ({FailureType}); dead-letter requested.",
                exception.GetType().Name);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // No requeue: the durable job is re-driven by the sweeper with bounded attempts.
            logger.LogWarning(
                "Stock Count adjustment delivery failed ({FailureType}); the sweeper will recover the job.",
                exception.GetType().Name);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken);
        }
    }
}
