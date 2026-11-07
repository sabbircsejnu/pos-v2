using RetailPOS.Application.Settings.Abstractions;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

/// <summary>
/// Adds the D10 posting intent and its <see cref="SaleFinancialPostingEventTypes.Requested"/> outbox event to the
/// caller's DbContext. Both are persisted by the caller's next save, inside the caller's transaction, so an
/// uncommitted sale, void or refund can never leave an intent or an event behind.
/// </summary>
public sealed class SaleFinancialPostingWriter(
    RetailPOSDbContext dbContext,
    ICashSessionEventWriter eventWriter,
    TimeProvider clock,
    ICompanyTimeZoneProvider timeZones,
    SaleFinancialPostingSignal? signal = null) : ISaleFinancialPostingWriter
{
    public async Task<SaleFinancialPosting> AppendSaleAsync(Sale sale, CancellationToken cancellationToken)
    {
        var posting = SaleFinancialIntentFactory.ForSale(
            sale,
            await ResolveBusinessIdAsync(sale, cancellationToken),
            clock.GetUtcNow().UtcDateTime);
        return await AddAsync(posting, sale, cancellationToken);
    }

    public async Task<SaleFinancialPosting> AppendReversalAsync(
        Sale sale,
        IReadOnlyList<SaleRefundLine> refundLines,
        string reversalType,
        long actorUserId,
        CancellationToken cancellationToken,
        SaleRefundPostingContext? refund = null)
    {
        var businessId = await ResolveBusinessIdAsync(sale, cancellationToken);
        // P3.2-D02: the reversal is dated by its own server time, taken here inside the void/refund transaction, in
        // the same effective Outlet/Business zone that dates cash sessions (D9).
        var posting = SaleFinancialIntentFactory.ForReversal(
            sale,
            businessId,
            refundLines,
            reversalType,
            actorUserId,
            clock.GetUtcNow().UtcDateTime,
            await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken),
            refund);
        return await AddAsync(posting, sale, cancellationToken);
    }

    public void NotifyCommitted() => signal?.Notify();

    private async Task<SaleFinancialPosting> AddAsync(
        SaleFinancialPosting posting,
        Sale sale,
        CancellationToken cancellationToken)
    {
        dbContext.SaleFinancialPostings.Add(posting);
        await eventWriter.AppendAsync(
            new CashSessionEvent(
                SaleFinancialPostingEventTypes.Requested,
                posting.BusinessId,
                nameof(SaleFinancialPosting),
                posting.SourceKey,
                "requested",
                posting.CreatedAtUtc,
                new
                {
                    businessId = posting.BusinessId,
                    saleId = posting.SaleId,
                    saleNumber = sale.SaleNumber,
                    kind = posting.Kind,
                    reversalType = posting.ReversalType,
                    sourceKey = posting.SourceKey,
                    outletId = posting.OutletId,
                    terminalId = posting.TerminalId,
                    cashSessionId = posting.CashSessionId,
                    businessDate = DateOnly.FromDateTime(posting.BusinessDate),
                    snapshotHash = posting.SnapshotHash
                }),
            cancellationToken);
        return posting;
    }

    // Sales created before Business tenancy may carry no Business of their own; their outlet decides.
    private async Task<long> ResolveBusinessIdAsync(Sale sale, CancellationToken cancellationToken) =>
        sale.BusinessId
        ?? await dbContext.Outlets.AsNoTracking()
            .Where(outlet => outlet.Id == sale.OutletId)
            .Select(outlet => outlet.BusinessId)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException($"Sale {sale.Id} has no Business to post its accounting to.");
}

/// <summary>
/// In-process wake-up for the posting worker after a source transaction commits. It only shortens latency: PostgreSQL
/// remains authoritative and the worker's poll finds every due posting without it (also across instances).
/// </summary>
public sealed class SaleFinancialPostingSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signalled.
            }
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);
}
