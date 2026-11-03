using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Integration;
using RetailPOS.Core.Entities.Integration;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Integration;

public sealed class OutboxDispatcher(RetailPOSDbContext db, IConfirmedIntegrationPublisher publisher,
    DurableDeliveryOptions options, TimeProvider clock, ILogger<OutboxDispatcher> logger)
{
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        options.Validate();
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Outbox leases require PostgreSQL row locks.");
        var now = clock.GetUtcNow().UtcDateTime;
        var owner = Guid.NewGuid();
        List<IntegrationOutboxMessage> rows;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.Set<IntegrationOutboxMessage>().Where(message =>
                message.Status == "Leased" && message.LeaseExpiresAtUtc <= now
                    && message.AttemptCount >= options.MaxAttempts)
                .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.Status, "DeadLetter")
                    .SetProperty(message => message.LastErrorCode, "lease_attempts_exhausted")
                    .SetProperty(message => message.LeaseOwner, (Guid?)null)
                    .SetProperty(message => message.LeaseExpiresAtUtc, (DateTime?)null), cancellationToken);
            rows = await db.Set<IntegrationOutboxMessage>().FromSqlInterpolated($"""
                SELECT * FROM integration_outbox_messages
                WHERE (status = 'Pending' AND next_attempt_at_utc <= {now})
                   OR (status = 'Leased' AND lease_expires_at_utc <= {now})
                ORDER BY next_attempt_at_utc, id LIMIT {options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                row.Status = "Leased"; row.LeaseOwner = owner;
                row.LeaseExpiresAtUtc = now.AddSeconds(options.LeaseSeconds); row.AttemptCount++;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        foreach (var row in rows)
        {
            var envelope = ToEnvelope(row);
            string? error = null;
            try
            {
                envelope.Validate();
                if (envelope.EnvelopeHash != row.EnvelopeHash)
                    throw new IntegrationPoisonException("Stored envelope has changed since enqueue.");
                await publisher.PublishAsync(envelope, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (IntegrationPoisonException) { error = "poison"; }
            catch (InvalidDataException) { error = "invalid_payload"; }
            catch (System.Text.Json.JsonException) { error = "invalid_payload"; }
            catch (Exception exception) when (exception is IOException or TimeoutException
                or RabbitMQ.Client.Exceptions.RabbitMQClientException)
            {
                error = "transport";
                logger.LogWarning("Outbox transport retry for message {MessageId}, attempt {Attempt}.", row.Id, row.AttemptCount);
            }
            var completedAt = clock.GetUtcNow().UtcDateTime;
            var dead = error == "poison" || error == "invalid_payload" || row.AttemptCount >= options.MaxAttempts;
            var backoff = Math.Min(options.MaxRetrySeconds,
                options.BaseRetrySeconds * Math.Pow(2, Math.Min(row.AttemptCount - 1, 20)));
            await db.Set<IntegrationOutboxMessage>().Where(message =>
                    message.Id == row.Id && message.LeaseOwner == owner && message.Status == "Leased")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.Status, error == null ? "Published" : dead ? "DeadLetter" : "Pending")
                    .SetProperty(message => message.PublishedAtUtc, error == null ? completedAt : (DateTime?)null)
                    .SetProperty(message => message.LastErrorCode, error)
                    .SetProperty(message => message.NextAttemptAtUtc, completedAt.AddSeconds(backoff + Random.Shared.NextDouble()))
                    .SetProperty(message => message.LeaseOwner, (Guid?)null)
                    .SetProperty(message => message.LeaseExpiresAtUtc, (DateTime?)null), cancellationToken);
        }
        db.ChangeTracker.Clear();
        return rows.Count;
    }

    internal static IntegrationEnvelope ToEnvelope(IntegrationOutboxMessage row) => new(
        row.Id, row.BusinessId, row.Producer, row.MessageType, row.SchemaVersion, row.AggregateType,
        row.AggregateId, row.CorrelationId, row.CausationId,
        row.OccurredAtUtc.Kind == DateTimeKind.Local ? row.OccurredAtUtc.ToUniversalTime()
            : DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc),
        row.PayloadJson, row.PayloadHash, row.SourceVersion);
}
