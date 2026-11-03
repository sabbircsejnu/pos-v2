using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

/// <summary>
/// D1/D7 exactly-once cash effects. Lock order: session (FOR SHARE) first, then the per-source advisory lock, so two
/// retries of the same operation serialise and the second finds the committed movement instead of inserting again.
/// </summary>
public sealed class CashSessionMovementRecorder(
    RetailPOSDbContext dbContext,
    ICashSessionStore store,
    ICashSessionEventWriter eventWriter,
    TimeProvider clock) : ICashSessionMovementRecorder
{
    public async Task<CashSessionMovementResult> RecordAsync(
        CashSessionMovementRequest request,
        CancellationToken cancellationToken)
    {
        if (!CashSessionMovementTypes.All.Contains(request.MovementType))
            throw new ArgumentException($"Unsupported cash movement type '{request.MovementType}'.");
        if (request.CashEffect == 0 || decimal.Round(request.CashEffect, 2) != request.CashEffect)
            throw new ArgumentException("A cash movement needs a non-zero amount with at most two decimals.");
        if (string.IsNullOrWhiteSpace(request.SourceKey) || request.SourceKey.Length > 120)
            throw new ArgumentException("A cash movement needs a source key of at most 120 characters.");

        var session = await store.LockAsync(request.CashSessionId, CashSessionLockMode.Share, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
        var now = clock.GetUtcNow().UtcDateTime;
        var businessId = session.BusinessId ?? throw new InvalidOperationException("Cash session has no Business scope.");

        if (dbContext.Database.IsNpgsql())
        {
            var lockKey = $"cash-movement:{businessId}:{request.SourceKey}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        }

        var existing = await dbContext.CashSessionMovements.AsNoTracking().FirstOrDefaultAsync(
            movement => movement.BusinessId == businessId && movement.SourceKey == request.SourceKey,
            cancellationToken);
        if (existing is not null)
            return new CashSessionMovementResult(existing, false);

        if (session.Status != CashSessionStatuses.Open)
            throw new CashSessionException(
                CashSessionErrorCodes.Closed,
                "The cash session is closed and cannot receive new cash movements.",
                409);
        if (session.IsOverdueAt(now))
            throw new CashSessionException(
                CashSessionErrorCodes.Overdue,
                "The cash session has reached its time limit. Close it before recording more cash movements.",
                409);

        var movement = new CashSessionMovement
        {
            BusinessId = businessId,
            OutletId = session.OutletId,
            CashSessionId = session.Id,
            MovementType = request.MovementType,
            CashEffect = request.CashEffect,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            SourceKey = request.SourceKey,
            OriginalSaleId = request.OriginalSaleId,
            OriginalCashSessionId = request.OriginalCashSessionId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            RecordedAtUtc = now,
            RecordedByUserId = request.ActorUserId
        };
        dbContext.CashSessionMovements.Add(movement);
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventWriter.AppendAsync(
            new CashSessionEvent(
                CashSessionEventTypes.MovementRecorded,
                businessId,
                "CashSession",
                session.Id.ToString(),
                $"movement:{movement.Id}",
                now,
                new
                {
                    businessId,
                    cashSessionId = session.Id,
                    outletId = session.OutletId,
                    movementId = movement.Id,
                    movementType = movement.MovementType,
                    cashEffect = movement.CashEffect,
                    sourceType = movement.SourceType,
                    sourceId = movement.SourceId,
                    originalSaleId = movement.OriginalSaleId,
                    originalCashSessionId = movement.OriginalCashSessionId,
                    recordedByUserId = movement.RecordedByUserId
                }),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CashSessionMovementResult(movement, true);
    }
}
