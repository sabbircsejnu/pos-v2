using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.Integration;
using RetailPOS.Core.Entities.Integration;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Integration;

public sealed class IntegrationInbox(
    RetailPOSDbContext db, AuditContext audit, IRequiredAuditTransaction transaction, TimeProvider clock)
{
    public async Task<string?> ConsumeAsync(IntegrationEnvelope envelope, IIntegrationConsumer consumer,
        CancellationToken cancellationToken)
    {
        envelope.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer.Name);
        if (consumer.Name.Length > 100) throw new ArgumentException("Consumer identity exceeds its bound.");
        consumer.ValidateTrustedEnvelope(envelope);
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Concurrent inbox identity requires PostgreSQL.");
        audit.Initialize(AuditContext.ForBusinessSystemJob(consumer.Name, envelope.BusinessId, envelope.CorrelationId));
        return await transaction.ExecuteAsync(new RequiredAuditOperation(envelope.BusinessId, "consume",
            "integration", "Consume durable integration message", "IntegrationInboxMessage", envelope.MessageId.ToString()),
            async token =>
            {
                var lockIdentity = $"{envelope.BusinessId}:{consumer.Name}:{envelope.MessageId:N}";
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockIdentity}, 0))", token);
                var prior = await db.Set<IntegrationInboxMessage>().SingleOrDefaultAsync(row =>
                    row.BusinessId == envelope.BusinessId && row.ConsumerName == consumer.Name
                        && row.MessageId == envelope.MessageId, token);
                if (prior is not null)
                {
                    if (prior.EnvelopeHash != envelope.EnvelopeHash)
                        throw new IntegrationConflictException("Inbox identity was reused with different content.");
                    return prior.EffectReference;
                }
                var effect = await consumer.ApplyAsync(envelope, token);
                db.Set<IntegrationInboxMessage>().Add(new IntegrationInboxMessage
                {
                    BusinessId = envelope.BusinessId, ConsumerName = consumer.Name, MessageId = envelope.MessageId,
                    MessageType = envelope.MessageType, SchemaVersion = envelope.SchemaVersion,
                    PayloadHash = envelope.PayloadHash, EnvelopeHash = envelope.EnvelopeHash,
                    ProcessedAtUtc = clock.GetUtcNow().UtcDateTime, EffectReference = effect
                });
                return effect;
            }, cancellationToken);
    }
}
