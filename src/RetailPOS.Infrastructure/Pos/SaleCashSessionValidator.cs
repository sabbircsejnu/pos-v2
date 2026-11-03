using Microsoft.Extensions.Logging;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.Pos;

/// <summary>
/// The sale side of the sale-versus-close serialisation (LLD §21.2): the session row is read with <c>FOR SHARE</c>
/// inside the sale transaction, status and the UTC cutoff are re-checked on the locked row, and the cutoff is checked
/// again as the last step before commit. A close that started first holds <c>FOR UPDATE</c>, so the sale waits and
/// then sees <c>closed</c>; a sale that took the shared lock first makes the close wait, so its facts are in the summary.
/// The guarantee is "decision taken while holding the lock, immediately before commit" — not a physical commit-time one.
/// </summary>
public sealed class SaleCashSessionValidator(
    ICashSessionStore store,
    IAuditService auditService,
    ICashSessionExpiryPublisher eventPublisher,
    TimeProvider clock,
    ILogger<SaleCashSessionValidator> logger) : ISaleCashSessionValidator
{
    public async Task<CashSession> ValidateActiveForSaleAsync(
        long outletId,
        long terminalId,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        var sessionId = await store.FindOpenSessionIdAsync(outletId, terminalId, cancellationToken)
            ?? throw new CashSessionException(
                CashSessionErrorCodes.NoActiveSession,
                "No active cash session found for this terminal. Please open cash before completing sale.");

        var session = await store.LockAsync(sessionId, CashSessionLockMode.Share, cancellationToken);
        if (session is null || session.Status != CashSessionStatuses.Open)
            throw new CashSessionException(
                CashSessionErrorCodes.Closed,
                "The cash session was closed. Please open cash before completing sale.");

        var now = clock.GetUtcNow().UtcDateTime;
        if (session.IsOverdueAt(now))
        {
            await RecordBlockedAsync(session, now, actorUserId, cancellationToken);
            throw Overdue(session);
        }

        return session;
    }

    public Task ValidateFinalGateAsync(CashSession session, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (session.IsOverdueAt(now))
            throw Overdue(session);
        return Task.CompletedTask;
    }

    private static CashSessionException Overdue(CashSession session) =>
        new(
            CashSessionErrorCodes.Overdue,
            $"This cash session has reached its {session.DurationSnapshotHours}-hour limit.\n\nPlease close the current cash session before continuing.",
            409);

    private async Task RecordBlockedAsync(
        CashSession session,
        DateTime checkedAt,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        await RecordExpiredSessionAuditAsync(session, checkedAt, actorUserId, cancellationToken);

        _ = eventPublisher.PublishCashSessionExpiredAsync(
            new CashSessionExpiredNotice(session.Id, session.OutletId, session.TerminalId, session.OpenedAt, checkedAt),
            cancellationToken);
        _ = eventPublisher.PublishSaleBlockedByExpiredSessionAsync(
            new SaleBlockedByExpiredSessionNotice(session.OutletId, session.TerminalId, session.Id, checkedAt),
            cancellationToken);
    }

    private async Task RecordExpiredSessionAuditAsync(
        CashSession session,
        DateTime checkedAt,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Sales,
                Summary = "POS sale blocked because cash session is overdue.",
                PrimaryEntity = ("CashSession", session.Id.ToString()),
                Status = AuditStatus.Failed,
                ErrorMessage = CashSessionErrorCodes.Overdue,
                Metadata = new Dictionary<string, object?>
                {
                    ["maxDurationHours"] = session.DurationSnapshotHours,
                    ["checkedAt"] = checkedAt,
                    ["cutoffAtUtc"] = session.CutoffAtUtc,
                    ["actorUserId"] = actorUserId,
                    ["action"] = "SaleBlockedByExpiredSession",
                    ["cashSessionId"] = session.Id,
                    ["outletId"] = session.OutletId,
                    ["terminalId"] = session.TerminalId,
                    ["businessDate"] = session.BusinessDate,
                    ["openedAt"] = session.OpenedAt
                }
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Failed to record cash session audit action {Action} for session {SessionId}",
                "SaleBlockedByExpiredSession",
                session.Id);
        }
    }
}
