using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Integration;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Integration;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Integration;

public sealed class IntegrationOutbox(RetailPOSDbContext db, IAuditContext audit, TimeProvider clock,
    IIntegrationReplayAuthorization replayAuthorization) : IIntegrationOutbox
{
    public async Task AppendAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken)
    {
        envelope.Validate();
        RequireTransaction(envelope.BusinessId);
        await AddIfNewAsync(envelope, cancellationToken);
    }

    public async Task AppendSourceChangeAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken)
    {
        envelope.Validate();
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Source changes and their outbox event must share a database transaction.");
        if (audit.IsInitialized && audit.BusinessId.HasValue && audit.BusinessId != envelope.BusinessId)
            throw new UnauthorizedAccessException("Source event Business does not match the active audit Business.");
        await AddIfNewAsync(envelope, cancellationToken);
    }

    private async Task AddIfNewAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken)
    {
        var existing = await db.Set<IntegrationOutboxMessage>().FindAsync([envelope.MessageId], cancellationToken);
        if (existing is not null)
        {
            if (existing.BusinessId != envelope.BusinessId || existing.EnvelopeHash != envelope.EnvelopeHash)
                throw new IntegrationConflictException("Outbox identity was reused with different content.");
            return;
        }
        db.Set<IntegrationOutboxMessage>().Add(new IntegrationOutboxMessage
        {
            Id = envelope.MessageId, BusinessId = envelope.BusinessId, Producer = envelope.Producer,
            MessageType = envelope.MessageType, SchemaVersion = envelope.SchemaVersion,
            SourceVersion = envelope.SourceVersion,
            AggregateType = envelope.AggregateType, AggregateId = envelope.AggregateId,
            CorrelationId = envelope.CorrelationId, CausationId = envelope.CausationId,
            OccurredAtUtc = envelope.OccurredAtUtc, PayloadJson = envelope.PayloadJson,
            PayloadHash = envelope.PayloadHash, EnvelopeHash = envelope.EnvelopeHash,
            NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime
        });
    }

    public async Task ReplayAsync(long businessId, Guid messageId, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await replayAuthorization.DemandAsync(businessId, cancellationToken);
        RequireTransaction(businessId);
        if (audit.ScopeActionType != "replay" || audit.ScopePrimaryEntityId != messageId.ToString()
            || audit.ScopeActionSummary != reason)
            throw new InvalidOperationException("Replay requires an explicit reason-bearing Audit v2 replay scope.");
        var row = await db.Set<IntegrationOutboxMessage>().SingleOrDefaultAsync(
            message => message.BusinessId == businessId && message.Id == messageId, cancellationToken)
            ?? throw new KeyNotFoundException("Outbox message not found.");
        if (row.Status != "DeadLetter") throw new InvalidOperationException("Only dead-letter messages can be replayed.");
        row.Status = "Pending"; row.AttemptCount = 0; row.LastErrorCode = null;
        row.NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime;
        // The caller supplies permission/reason and records the replay in its required Audit v2 boundary.
    }

    private void RequireTransaction(long businessId)
    {
        if (db.Database.CurrentTransaction is null || !audit.RequiresAtomicPersistence
            || audit.BusinessId != businessId)
            throw new InvalidOperationException("Outbox append/replay requires the source's scoped required audit transaction.");
    }
}

public sealed class DisabledIntegrationReplayAuthorization : IIntegrationReplayAuthorization
{
    public Task DemandAsync(long businessId, CancellationToken cancellationToken) =>
        throw new UnauthorizedAccessException("Operator replay is disabled pending approved replay authorization/topology policy.");
}
