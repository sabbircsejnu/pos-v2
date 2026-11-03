using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Infrastructure.StockCounts;

namespace RetailPOS.Infrastructure.Integration;

/// <summary>
/// Handles one RabbitMQ delivery of a Mobile Stock Take submission request. PostgreSQL stays authoritative:
/// every outcome other than a poison message is acknowledged because the job is durably recoverable by the
/// sweeper, so a failed delivery can never become an infinite redelivery loop or a lost job.
/// </summary>
public sealed class MobileStockTakeSubmissionDelivery(
    IMobileStockTakeSubmissionProcessor processor,
    ILogger<MobileStockTakeSubmissionDelivery> logger)
{
    public const string QueueName = "retailpos.mobile-stock-take.submission";

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
            var payload = MobileStockTakeSubmissionMessage.Deserialize(envelope.PayloadJson);
            if (envelope.Producer != MobileStockTakeSubmissionMessage.Producer
                || envelope.MessageType != MobileStockTakeSubmissionMessage.MessageType
                || envelope.SchemaVersion != MobileStockTakeSubmissionMessage.SchemaVersion
                || envelope.AggregateType != MobileStockTakeSubmissionMessage.AggregateType
                || payload is null
                || payload.BusinessId != envelope.BusinessId
                || payload.JobId == Guid.Empty
                || envelope.AggregateId != payload.JobId.ToString())
            {
                throw new IntegrationPoisonException("Unsupported Mobile Stock Take submission message.");
            }

            var outcome = await processor.ProcessAsync(payload.JobId, envelope.BusinessId, cancellationToken);
            logger.LogInformation(
                "Mobile Stock Take submission job {JobId} delivery outcome {Outcome}.",
                payload.JobId,
                outcome);
            await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception) when (exception is IntegrationPoisonException
            or InvalidDataException or JsonException)
        {
            logger.LogError(
                "Mobile Stock Take submission delivery rejected ({FailureType}); dead-letter requested.",
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
                "Mobile Stock Take submission delivery failed ({FailureType}); the sweeper will recover the job.",
                exception.GetType().Name);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken);
        }
    }
}
