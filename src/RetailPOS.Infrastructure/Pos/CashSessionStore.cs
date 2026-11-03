using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

public sealed class CashSessionStore(RetailPOSDbContext dbContext) : ICashSessionStore
{
    private const string OpenSessionIndexName = "ux_cash_sessions_one_open_per_terminal";
    private const string IdempotencyIndexName = "ux_cash_sessions_open_idempotency";

    public async Task<ICashSessionTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null)
            return NoopTransaction.Instance;

        var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        return new EfTransaction(transaction);
    }

    public Task<long?> GetOutletIdAsync(long sessionId, CancellationToken cancellationToken) =>
        dbContext.CashSessions
            .AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => (long?)session.OutletId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<CashSessionOpenContext?> GetOpenContextAsync(long outletId, CancellationToken cancellationToken)
    {
        var outlet = await dbContext.Outlets
            .AsNoTracking()
            .Where(candidate => candidate.Id == outletId)
            .Select(candidate => new { candidate.BusinessId, candidate.TimeZoneId, candidate.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (outlet?.BusinessId is null)
            return null;

        var terminals = await dbContext.PosTerminals
            .AsNoTracking()
            .Where(terminal => terminal.OutletId == outletId && terminal.IsActive)
            .OrderBy(terminal => terminal.Id)
            .Select(terminal => new CashSessionTerminalInfo(terminal.Id, terminal.Name, terminal.Code))
            .ToListAsync(cancellationToken);
        var occupied = await dbContext.CashSessions
            .AsNoTracking()
            .Where(session => session.OutletId == outletId && session.Status == CashSessionStatuses.Open)
            .Select(session => session.TerminalId)
            .ToListAsync(cancellationToken);
        return new CashSessionOpenContext(outlet.BusinessId.Value, outlet.TimeZoneId, terminals, outlet.Name, occupied);
    }

    public Task<CashSession?> FindByIdempotencyKeyAsync(long outletId, string key, CancellationToken cancellationToken) =>
        dbContext.CashSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                session => session.OutletId == outletId && session.OpenIdempotencyKey == key,
                cancellationToken);

    public Task<long?> FindOpenSessionIdAsync(long outletId, long terminalId, CancellationToken cancellationToken) =>
        dbContext.CashSessions
            .AsNoTracking()
            .Where(session => session.OutletId == outletId
                && session.TerminalId == terminalId
                && session.Status == CashSessionStatuses.Open)
            .OrderByDescending(session => session.OpenedAt)
            .Select(session => (long?)session.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<long>> ListOpenSessionIdsAsync(long outletId, CancellationToken cancellationToken) =>
        await dbContext.CashSessions
            .AsNoTracking()
            .Where(session => session.OutletId == outletId && session.Status == CashSessionStatuses.Open)
            .OrderBy(session => session.Id)
            .Select(session => session.Id)
            .ToListAsync(cancellationToken);

    public Task<CashSession?> FindAsync(long sessionId, CancellationToken cancellationToken) =>
        dbContext.CashSessions.AsNoTracking().FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public async Task<CashSession?> LockAsync(
        long sessionId,
        CashSessionLockMode mode,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
            return await FindAsync(sessionId, cancellationToken);

        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A cash session row lock requires an open database transaction.");

        // The lock is taken in SQL so that the status and the immutable duration snapshot are read after it is held.
        var query = mode == CashSessionLockMode.Update
            ? dbContext.CashSessions.FromSqlInterpolated($"SELECT * FROM cash_sessions WHERE id = {sessionId} FOR UPDATE")
            : dbContext.CashSessions.FromSqlInterpolated($"SELECT * FROM cash_sessions WHERE id = {sessionId} FOR SHARE");
        return await query.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CashSessionAddResult> AddOpenSessionAsync(CashSession session, CancellationToken cancellationToken)
    {
        // Fast path for every provider; the unique indexes remain the authority under a race.
        var occupied = await dbContext.CashSessions.AsNoTracking().AnyAsync(
            candidate => candidate.OutletId == session.OutletId
                && candidate.TerminalId == session.TerminalId
                && candidate.Status == CashSessionStatuses.Open,
            cancellationToken);
        if (occupied)
            return CashSessionAddResult.TerminalAlreadyOpen;

        dbContext.CashSessions.Add(session);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return CashSessionAddResult.Created;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } violation)
        {
            dbContext.Entry(session).State = EntityState.Detached;
            return violation.ConstraintName switch
            {
                OpenSessionIndexName => CashSessionAddResult.TerminalAlreadyOpen,
                IdempotencyIndexName => CashSessionAddResult.IdempotencyReplay,
                _ => throw exception
            };
        }
        catch (DbUpdateException exception) when (
            exception.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true)
        {
            dbContext.Entry(session).State = EntityState.Detached;
            return exception.InnerException.Message.Contains("open_idempotency_key", StringComparison.OrdinalIgnoreCase)
                ? CashSessionAddResult.IdempotencyReplay
                : CashSessionAddResult.TerminalAlreadyOpen;
        }
    }

    public async Task<CashSessionFacts?> GetFactsAsync(long sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.CashSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == sessionId, cancellationToken);
        if (session is null)
            return null;

        var sales = await dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.CashSessionId == sessionId)
            .Select(sale => new CashSessionSaleFact(
                sale.Id,
                sale.Status,
                sale.PaymentMethod,
                sale.TotalAmount,
                sale.Payments
                    .Select(payment => new CashSessionPaymentFact(
                        payment.Method,
                        payment.Amount,
                        payment.Tendered,
                        payment.ChangeGiven,
                        payment.NetCashRetained))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var movements = await dbContext.CashSessionMovements
            .AsNoTracking()
            .Where(movement => movement.CashSessionId == sessionId)
            .OrderBy(movement => movement.Id)
            .Select(movement => new CashSessionMovementFact(
                movement.Id,
                movement.MovementType,
                movement.CashEffect,
                movement.SourceType,
                movement.SourceId,
                movement.OriginalSaleId,
                movement.OriginalCashSessionId,
                movement.RecordedAtUtc))
            .ToListAsync(cancellationToken);

        return new CashSessionFacts(CashSessionMapper_ToSnapshot(session), sales, movements);
    }

    public async Task<bool> MarkClosedAsync(CashSessionClosePersist close, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var tracked = await dbContext.CashSessions.FirstOrDefaultAsync(
                session => session.Id == close.SessionId && session.Status == CashSessionStatuses.Open,
                cancellationToken);
            if (tracked is null)
                return false;
            tracked.Status = CashSessionStatuses.Closed;
            tracked.ClosedAt = close.ClosedAtUtc;
            tracked.ClosedByUserId = close.ClosedByUserId;
            tracked.ExpectedClosingCashAmount = close.ExpectedClosingCash;
            tracked.ActualClosingCashAmount = close.CountedCash;
            tracked.DifferenceAmount = close.Variance;
            tracked.ClosingNotes = close.ClosingNotes;
            tracked.VarianceReason = close.VarianceReason;
            tracked.UpdatedAt = close.ClosedAtUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        return await dbContext.CashSessions
            .Where(session => session.Id == close.SessionId && session.Status == CashSessionStatuses.Open)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.Status, CashSessionStatuses.Closed)
                    .SetProperty(session => session.ClosedAt, close.ClosedAtUtc)
                    .SetProperty(session => session.ClosedByUserId, close.ClosedByUserId)
                    .SetProperty(session => session.ExpectedClosingCashAmount, close.ExpectedClosingCash)
                    .SetProperty(session => session.ActualClosingCashAmount, close.CountedCash)
                    .SetProperty(session => session.DifferenceAmount, close.Variance)
                    .SetProperty(session => session.ClosingNotes, close.ClosingNotes)
                    .SetProperty(session => session.VarianceReason, close.VarianceReason)
                    .SetProperty(session => session.UpdatedAt, close.ClosedAtUtc),
                cancellationToken) == 1;
    }

    public Task AddCloseEvidenceAsync(
        CashSessionCloseSnapshot snapshot,
        IReadOnlyList<CashSessionHandover> handovers,
        CashDifferenceApproval? approval,
        CancellationToken cancellationToken)
    {
        dbContext.CashSessionCloseSnapshots.Add(snapshot);
        dbContext.CashSessionHandovers.AddRange(handovers);
        if (approval is not null)
            dbContext.CashDifferenceApprovals.Add(approval);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private static CashSessionSnapshot CashSessionMapper_ToSnapshot(CashSession session) =>
        RetailPOS.Application.Pos.CashSessionMapper.ToSnapshot(session);

    private sealed class EfTransaction(IDbContextTransaction transaction) : ICashSessionTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) => transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class NoopTransaction : ICashSessionTransaction
    {
        public static NoopTransaction Instance { get; } = new();

        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
