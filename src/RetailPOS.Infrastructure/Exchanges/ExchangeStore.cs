using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Exchanges;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Pos;

namespace RetailPOS.Infrastructure.Exchanges;

/// <summary>
/// Exchange persistence on the request's scoped <see cref="RetailPOSDbContext"/>. Every query is business-filtered by its
/// arguments; row locks follow the Stage 0 order and use parameters only. EF Contains uses lists (C# 14 / EF 9 note).
/// </summary>
public sealed class ExchangeStore(RetailPOSDbContext dbContext) : IExchangeStore
{
    public async Task<IExchangeTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null)
            return new AmbientTransaction();
        return new OwnedTransaction(dbContext, await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken));
    }

    public Task<ExchangePolicyVersion?> FindCurrentPolicyAsync(long businessId, CancellationToken cancellationToken) =>
        dbContext.ExchangePolicyVersions.AsNoTracking()
            .Where(policy => policy.BusinessId == businessId)
            .OrderByDescending(policy => policy.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddPolicyAsync(ExchangePolicyVersion policy, CancellationToken cancellationToken) =>
        await dbContext.ExchangePolicyVersions.AddAsync(policy, cancellationToken);

    public Task<long?> FindSaleIdByInvoiceNumberAsync(long businessId, string invoiceNumber, CancellationToken cancellationToken) =>
        dbContext.Sales.AsNoTracking()
            .Where(sale => sale.BusinessId == businessId && sale.SaleNumber == invoiceNumber)
            .Select(sale => (long?)sale.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> LoadSourceSalesAsync(
        long businessId, IReadOnlyCollection<long> saleIds, bool forUpdate, CancellationToken cancellationToken)
    {
        var ids = saleIds.Distinct().OrderBy(id => id).ToList();
        if (ids.Count == 0)
            return [];
        if (forUpdate && dbContext.Database.IsNpgsql())
        {
            var array = ids.ToArray();
            // Same lock the refund takes (sale row FOR UPDATE), ascending, so refunds and exchanges serialise per sale (C-03).
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM sales WHERE business_id = {businessId} AND id = ANY({array}) ORDER BY id FOR UPDATE", cancellationToken);
        }

        var query = dbContext.Sales
            .Include(sale => sale.Customer)
            .Include(sale => sale.Outlet)
            .Include(sale => sale.Items).ThenInclude(item => item.Components)
            .Include(sale => sale.Items).ThenInclude(item => item.Variant).ThenInclude(variant => variant.Product)
            .AsSplitQuery()
            .Where(sale => sale.BusinessId == businessId && ids.Contains(sale.Id));
        return await (forUpdate ? query : query.AsNoTracking()).OrderBy(sale => sale.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExchangeReplacementLine>> LoadReplacementLinesAsync(
        long businessId, IReadOnlyCollection<long> lineIds, bool forUpdate, CancellationToken cancellationToken)
    {
        var ids = lineIds.Distinct().OrderBy(id => id).ToList();
        if (ids.Count == 0)
            return [];
        if (forUpdate && dbContext.Database.IsNpgsql())
        {
            var array = ids.ToArray();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT line.id FROM exchange_replacement_lines line JOIN exchanges exchange ON exchange.id = line.exchange_id WHERE exchange.business_id = {businessId} AND line.id = ANY({array}) ORDER BY line.id FOR UPDATE OF line", cancellationToken);
        }

        var query = dbContext.ExchangeReplacementLines
            .Include(line => line.Exchange)
            .Include(line => line.Components)
            .Where(line => ids.Contains(line.Id) && line.Exchange!.BusinessId == businessId);
        return await (forUpdate ? query : query.AsNoTracking()).ToListAsync(cancellationToken);
    }

    public Task<long?> FindWalkInCustomerIdAsync(long businessId, CancellationToken cancellationToken) =>
        dbContext.Customers.AsNoTracking()
            .Where(customer => customer.BusinessId == businessId && customer.CustomerCode == Customer.WalkInCustomerCode)
            .Select(customer => (long?)customer.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, decimal>> ReadOutletStockAsync(
        long outletId, IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken)
    {
        var ids = variantIds.Distinct().ToList();
        return await dbContext.Inventories.AsNoTracking()
            .Where(row => row.LocationType == "outlet" && row.LocationId == outletId && ids.Contains(row.VariantId))
            .GroupBy(row => row.VariantId)
            .Select(group => new { group.Key, Quantity = group.Sum(row => row.Quantity) })
            .ToDictionaryAsync(entry => entry.Key, entry => entry.Quantity, cancellationToken);
    }

    public async Task<Dictionary<long, Inventory>> LockOutletInventoriesAsync(
        long outletId, IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken)
    {
        var array = variantIds.Distinct().ToArray();
        if (array.Length == 0)
            return [];
        var rows = dbContext.Database.IsNpgsql()
            // One statement, ordered by id: the same order EF uses for checkout's stock updates (Stage 0 §7.2).
            ? await dbContext.Inventories
                .FromSqlInterpolated($"SELECT *, xmin FROM inventories WHERE location_type = 'outlet' AND location_id = {outletId} AND variant_id = ANY({array}) ORDER BY id FOR UPDATE")
                .ToListAsync(cancellationToken)
            : await dbContext.Inventories
                .Where(row => row.LocationType == "outlet" && row.LocationId == outletId && array.ToList().Contains(row.VariantId))
                .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.VariantId).ToDictionary(group => group.Key, group => group.OrderBy(row => row.Id).First());
    }

    public async Task<Inventory> CreateOutletInventoryAsync(long variantId, long outletId, CancellationToken cancellationToken)
    {
        // A concurrent creation of the same row fails on the unique index; the caller maps it to a retryable conflict (T-05).
        var row = new Inventory { VariantId = variantId, LocationId = outletId, LocationType = "outlet", Quantity = 0m };
        await dbContext.Inventories.AddAsync(row, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row;
    }

    public Task<string?> FindOutletInvoicePrefixAsync(long outletId, CancellationToken cancellationToken) =>
        dbContext.Outlets.AsNoTracking().Where(outlet => outlet.Id == outletId).Select(outlet => outlet.InvoicePrefix).FirstOrDefaultAsync(cancellationToken);

    public async Task<long> AllocateExchangeSequenceAsync(long businessId, long outletId, DateTime businessDate, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Exchange number allocation requires an active database transaction.");
        var date = DateOnly.FromDateTime(businessDate.Date);
        var rows = await dbContext.Database
            .SqlQuery<long>($"""
                INSERT INTO exchange_number_counters (business_id, outlet_id, business_date, last_sequence)
                VALUES ({businessId}, {outletId}, {date}, 1)
                ON CONFLICT (business_id, outlet_id, business_date)
                DO UPDATE SET last_sequence = exchange_number_counters.last_sequence + 1
                RETURNING last_sequence AS "Value"
                """)
            .ToListAsync(cancellationToken);
        return rows.Single();
    }

    public async Task<ExchangeAccountingPeriodState> LockAccountingPeriodAsync(long businessId, DateTime businessDate, CancellationToken cancellationToken)
    {
        var date = businessDate.Date;
        // The same resolution the journal engine uses, then the rows are locked FOR SHARE and their status re-read, so a
        // concurrent lock/close waits for this exchange or this exchange sees the committed change (VI-18).
        var yearId = await dbContext.FinancialYears.AsNoTracking()
            .Where(entry => entry.BusinessId == businessId && entry.StartDate.Date <= date && entry.EndDate.Date >= date)
            .Select(entry => (long?)entry.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (yearId is null)
            return new ExchangeAccountingPeriodState(false, false, null, null);
        var periodId = await dbContext.AccountingPeriods.AsNoTracking()
            .Where(entry => entry.FinancialYearId == yearId && entry.StartDate.Date <= date && entry.EndDate.Date >= date)
            .Select(entry => (long?)entry.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (periodId is null)
            return new ExchangeAccountingPeriodState(false, false, null, null);

        var yearClosed = (await dbContext.Database
            .SqlQuery<bool>($"SELECT is_closed AS \"Value\" FROM financial_years WHERE id = {yearId} FOR SHARE")
            .ToListAsync(cancellationToken)).Single();
        var period = (await dbContext.Database
            .SqlQuery<PeriodRow>($"SELECT name AS \"Name\", status AS \"Status\" FROM accounting_periods WHERE id = {periodId} FOR SHARE")
            .ToListAsync(cancellationToken)).Single();
        var open = !yearClosed && string.Equals(period.Status, AccountingPeriodStatuses.Open, StringComparison.OrdinalIgnoreCase);
        return new ExchangeAccountingPeriodState(true, open, period.Name, yearClosed ? "in a closed financial year" : period.Status);
    }

    public async Task<IReadOnlyList<ExchangeApproval>> LockApprovalsAsync(long businessId, IReadOnlyCollection<string> tokenHashes, CancellationToken cancellationToken)
    {
        var array = tokenHashes.Distinct().ToArray();
        if (array.Length == 0)
            return [];
        return dbContext.Database.IsNpgsql()
            ? await dbContext.ExchangeApprovals
                .FromSqlInterpolated($"SELECT * FROM exchange_approvals WHERE business_id = {businessId} AND token_hash = ANY({array}) ORDER BY id FOR UPDATE")
                .ToListAsync(cancellationToken)
            : await dbContext.ExchangeApprovals.Where(approval => approval.BusinessId == businessId && array.ToList().Contains(approval.TokenHash)).ToListAsync(cancellationToken);
    }

    public Task AddApprovalsAsync(IReadOnlyCollection<ExchangeApproval> approvals, CancellationToken cancellationToken) =>
        dbContext.ExchangeApprovals.AddRangeAsync(approvals, cancellationToken);

    public Task<int> CountApprovalFailuresAsync(long businessId, string subjectKind, string subjectHash, DateTime sinceUtc, CancellationToken cancellationToken) =>
        dbContext.ExchangeApprovalFailures.AsNoTracking().CountAsync(failure =>
            failure.BusinessId == businessId && failure.SubjectKind == subjectKind && failure.SubjectHash == subjectHash
            && failure.AttemptedAtUtc >= sinceUtc, cancellationToken);

    public async Task RecordApprovalFailuresAsync(IReadOnlyCollection<ExchangeApprovalFailure> failures, CancellationToken cancellationToken)
    {
        await dbContext.ExchangeApprovalFailures.AddRangeAsync(failures, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        // Housekeeping: attempts older than a day can no longer count towards a lockout.
        var businessIds = failures.Select(failure => failure.BusinessId).Distinct().ToList();
        var hashes = failures.Select(failure => failure.SubjectHash).Distinct().ToList();
        var cutoff = DateTime.UtcNow.AddDays(-1);
        await dbContext.ExchangeApprovalFailures
            .Where(failure => businessIds.Contains(failure.BusinessId) && hashes.Contains(failure.SubjectHash) && failure.AttemptedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task ClearApprovalFailuresAsync(long businessId, IReadOnlyCollection<string> subjectHashes, CancellationToken cancellationToken)
    {
        var hashes = subjectHashes.ToList();
        return dbContext.ExchangeApprovalFailures
            .Where(failure => failure.BusinessId == businessId && hashes.Contains(failure.SubjectHash))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AcquireDrawerLockAsync(long businessId, long cashSessionId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
            return;
        var key = CashSessionMovementRecorder.DrawerLockKey(businessId, cashSessionId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public async Task<bool> LockActiveTerminalAsync(long businessId, long outletId, long terminalId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
            return await dbContext.PosTerminals.AsNoTracking().AnyAsync(terminal =>
                terminal.Id == terminalId && terminal.OutletId == outletId && terminal.IsActive && terminal.Outlet.BusinessId == businessId,
                cancellationToken);
        var rows = await dbContext.Database.SqlQuery<long>($@"SELECT t.id AS ""Value"" FROM pos_terminals t
            JOIN outlets o ON o.id = t.outlet_id
            WHERE t.id = {terminalId} AND t.outlet_id = {outletId} AND t.is_active AND o.business_id = {businessId}
            FOR SHARE OF t").ToListAsync(cancellationToken);
        return rows.Count == 1;
    }

    public Task<Exchange?> FindByIdempotencyKeyAsync(long businessId, string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.Exchanges.AsNoTracking()
            .FirstOrDefaultAsync(exchange => exchange.BusinessId == businessId && exchange.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<ExchangeReversal?> FindReversalByIdempotencyKeyAsync(long businessId, string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.ExchangeReversals.AsNoTracking()
            .FirstOrDefaultAsync(reversal => reversal.BusinessId == businessId && reversal.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<Exchange?> LoadExchangeForUpdateAsync(long businessId, long exchangeId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM exchanges WHERE business_id = {businessId} AND id = {exchangeId} FOR UPDATE", cancellationToken);
        return await dbContext.Exchanges
            .Include(exchange => exchange.SourceInvoices)
            .Include(exchange => exchange.ReturnLines)
            .Include(exchange => exchange.ReplacementLines).ThenInclude(line => line.Components)
            .Include(exchange => exchange.Settlements)
            .AsSplitQuery()
            .FirstOrDefaultAsync(exchange => exchange.BusinessId == businessId && exchange.Id == exchangeId, cancellationToken);
    }

    public Task<Exchange?> LoadExchangeAsync(long businessId, long exchangeId, CancellationToken cancellationToken) =>
        dbContext.Exchanges.AsNoTracking()
            .Include(exchange => exchange.SourceInvoices)
            .Include(exchange => exchange.ReturnLines)
            .Include(exchange => exchange.ReplacementLines).ThenInclude(line => line.Components)
            .AsSplitQuery()
            .FirstOrDefaultAsync(exchange => exchange.BusinessId == businessId && exchange.Id == exchangeId, cancellationToken);

    public async Task<IReadOnlyDictionary<long, decimal>> LockReplacementLineQuantitiesAsync(IReadOnlyCollection<long> lineIds, CancellationToken cancellationToken)
    {
        var array = lineIds.Distinct().OrderBy(id => id).ToArray();
        if (array.Length == 0)
            return new Dictionary<long, decimal>();
        if (!dbContext.Database.IsNpgsql())
        {
            var ids = array.ToList();
            return await dbContext.ExchangeReplacementLines.AsNoTracking().Where(line => ids.Contains(line.Id))
                .ToDictionaryAsync(line => line.Id, line => line.ReturnedQuantity, cancellationToken);
        }

        var rows = await dbContext.Database
            .SqlQuery<ReplacementQuantityRow>($"SELECT id AS \"Id\", returned_quantity AS \"ReturnedQuantity\" FROM exchange_replacement_lines WHERE id = ANY({array}) ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => row.ReturnedQuantity);
    }

    public async Task<IReadOnlyList<ExchangeStockMovement>> LoadStockMovementsAsync(long exchangeId, long outletId, CancellationToken cancellationToken) =>
        await dbContext.StockLedgers.AsNoTracking()
            .Where(row => row.ReferenceType == StockLedgerReferenceType.Exchange && row.ReferenceId == exchangeId
                && row.LocationType == "outlet" && row.LocationId == outletId)
            .OrderBy(row => row.Id)
            .Select(row => new ExchangeStockMovement(row.Id, row.VariantId, row.QtyIn, row.QtyOut, row.UnitCost, row.MovementValue))
            .ToListAsync(cancellationToken);

    public Task<ExchangeSalespersonAdjustment?> FindSalespersonAdjustmentAsync(long exchangeId, CancellationToken cancellationToken) =>
        dbContext.ExchangeSalespersonAdjustments.AsNoTracking()
            .FirstOrDefaultAsync(row => row.ExchangeId == exchangeId && row.ExchangeReversalId == null, cancellationToken);

    public Task<long> ReserveExchangeIdAsync(CancellationToken cancellationToken) => NextValueAsync("exchanges", cancellationToken);

    public Task<long> ReserveReversalIdAsync(CancellationToken cancellationToken) => NextValueAsync("exchange_reversals", cancellationToken);

    private async Task<long> NextValueAsync(string table, CancellationToken cancellationToken) =>
        table switch
        {
            "exchanges" => (await dbContext.Database.SqlQuery<long>($"SELECT nextval(pg_get_serial_sequence('exchanges', 'id')) AS \"Value\"").ToListAsync(cancellationToken)).Single(),
            "exchange_reversals" => (await dbContext.Database.SqlQuery<long>($"SELECT nextval(pg_get_serial_sequence('exchange_reversals', 'id')) AS \"Value\"").ToListAsync(cancellationToken)).Single(),
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };

    public async Task AddExchangeAsync(Exchange exchange, CancellationToken cancellationToken) =>
        await dbContext.Exchanges.AddAsync(exchange, cancellationToken);

    public Task AddLineageLinksAsync(IReadOnlyCollection<ExchangeLineageLink> links, CancellationToken cancellationToken) =>
        dbContext.ExchangeLineageLinks.AddRangeAsync(links, cancellationToken);

    public async Task AddSalespersonAdjustmentAsync(ExchangeSalespersonAdjustment adjustment, CancellationToken cancellationToken) =>
        await dbContext.ExchangeSalespersonAdjustments.AddAsync(adjustment, cancellationToken);

    public async Task AddReceiptSnapshotAsync(ExchangeReceiptSnapshot snapshot, CancellationToken cancellationToken) =>
        await dbContext.ExchangeReceiptSnapshots.AddAsync(snapshot, cancellationToken);

    public async Task AddReversalAsync(ExchangeReversal reversal, CancellationToken cancellationToken) =>
        await dbContext.ExchangeReversals.AddAsync(reversal, cancellationToken);

    public Task AddSettlementsAsync(IReadOnlyCollection<ExchangeSettlement> settlements, CancellationToken cancellationToken) =>
        dbContext.ExchangeSettlements.AddRangeAsync(settlements, cancellationToken);

    public async Task AddPrintHistoryAsync(ExchangeReceiptPrintHistory entry, CancellationToken cancellationToken) =>
        await dbContext.ExchangeReceiptPrintHistory.AddAsync(entry, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public bool IsUniqueViolation(Exception exception) =>
        Inner(exception) is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public bool IsConcurrencyFailure(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || Inner(exception) is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.LockNotAvailable };

    private static Exception? Inner(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException)
                return current;
        return null;
    }

    private sealed record PeriodRow(string Name, string Status);

    private sealed record ReplacementQuantityRow(long Id, decimal ReturnedQuantity);

    private sealed class AmbientTransaction : IExchangeTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class OwnedTransaction(RetailPOSDbContext context, IDbContextTransaction transaction) : IExchangeTransaction
    {
        private bool completed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            completed = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            if (completed)
                return;
            completed = true;
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            finally
            {
                // Shared services already saved; nothing they tracked may leak into a later unit of work (D10 precedent).
                context.ChangeTracker.Clear();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!completed)
                context.ChangeTracker.Clear();
            await transaction.DisposeAsync();
        }
    }
}
