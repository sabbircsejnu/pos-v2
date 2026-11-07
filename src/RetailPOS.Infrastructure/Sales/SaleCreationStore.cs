using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleCreationStore(
    RetailPOSDbContext dbContext,
    ISaleReadStore saleReadStore) : ISaleCreationStore
{
    public async Task<ISaleCreationTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new SaleCreationTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));

    public async Task<SaleIdempotencyMatch?> FindByIdempotencyKeyAsync(
        long outletId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var match = await dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.OutletId == outletId && sale.IdempotencyKey == idempotencyKey)
            .Select(sale => new { sale.Id, sale.IdempotencyFingerprint })
            .FirstOrDefaultAsync(cancellationToken);
        if (match is null)
            return null;

        var existing = await saleReadStore.FindByIdAsync(match.Id, cancellationToken);
        return existing is null ? null : new SaleIdempotencyMatch(existing, match.IdempotencyFingerprint);
    }

    public bool IsIdempotencyKeyViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                       (postgres.ConstraintName?.Contains("idempotency_key", StringComparison.OrdinalIgnoreCase) ?? false);

            // SQLite (used by relational unit tests) reports the columns in the message text.
            if (current.GetType().Name == "SqliteException" &&
                current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) &&
                current.Message.Contains("idempotency_key", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public async Task<IReadOnlyList<long>> FindUnsellableVariantIdsAsync(
        IReadOnlyCollection<long> variantIds,
        long businessId,
        CancellationToken cancellationToken)
    {
        var requested = variantIds.Distinct().ToList();
        var sellable = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                requested.Contains(variant.Id)
                && variant.BusinessId == businessId
                && variant.Product.Status == ProductStatus.Active)
            .Select(variant => variant.Id)
            .ToListAsync(cancellationToken);
        return requested.Except(sellable).ToList();
    }

    public Task<bool> IsEligibleSalespersonAsync(long userId, long businessId, long outletId, CancellationToken cancellationToken) =>
        SalespersonEligibility.Users(dbContext, businessId, outletId).AnyAsync(user => user.Id == userId, cancellationToken);

    public async Task<long?> GetOutletBusinessIdAsync(long outletId, CancellationToken cancellationToken) =>
        await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.Id == outletId)
            .Select(outlet => outlet.BusinessId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ExchangeOriginalSale?> FindExchangeOriginalSaleForUpdateAsync(
        long saleId,
        CancellationToken cancellationToken)
    {
        // Serialises two cashiers replacing the same original sale; relational test providers have no row locks.
        if (dbContext.Database.IsNpgsql() && dbContext.Database.CurrentTransaction is not null)
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM sales WHERE id = {saleId} FOR UPDATE",
                cancellationToken);

        var sale = await dbContext.Sales.FirstOrDefaultAsync(item => item.Id == saleId, cancellationToken);
        if (sale is null)
            return null;

        var businessId = await GetOutletBusinessIdAsync(sale.OutletId, cancellationToken);
        return new ExchangeOriginalSale(sale, businessId);
    }

    public Task<Customer?> FindCustomerAsync(long customerId, long businessId, CancellationToken cancellationToken) =>
        dbContext.Customers.FirstOrDefaultAsync(
            customer => customer.Id == customerId && customer.BusinessId == businessId,
            cancellationToken);

    public Task<Customer?> FindWalkInCustomerAsync(long businessId, CancellationToken cancellationToken) =>
        dbContext.Customers.FirstOrDefaultAsync(
            customer => customer.BusinessId == businessId && customer.CustomerCode == Customer.WalkInCustomerCode,
            cancellationToken);

    public async Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken) =>
        await dbContext.Customers.AddAsync(customer, cancellationToken);

    public Task<Inventory?> FindOutletInventoryAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken) =>
        dbContext.Inventories.FirstOrDefaultAsync(
            inventory => inventory.VariantId == variantId &&
                         inventory.LocationId == outletId &&
                         inventory.LocationType == "outlet",
            cancellationToken);

    public async Task<long?> ResolveActiveTerminalIdAsync(
        long outletId,
        long? requestedTerminalId,
        CancellationToken cancellationToken)
    {
        if (requestedTerminalId.HasValue)
        {
            var terminal = await dbContext.PosTerminals
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.Id == requestedTerminalId.Value &&
                            item.OutletId == outletId &&
                            item.IsActive,
                    cancellationToken);
            if (terminal is null)
                throw new InvalidOperationException(
                    $"Terminal {requestedTerminalId.Value} is not active for outlet {outletId}.");
            return terminal.Id;
        }

        // No implicit default: a lone active terminal is unambiguous, several require an explicit choice.
        var activeTerminalIds = await dbContext.PosTerminals
            .AsNoTracking()
            .Where(item => item.OutletId == outletId && item.IsActive)
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (activeTerminalIds.Count > 1)
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.TerminalRequired,
                "This outlet has several active terminals. Select the terminal to use before completing the sale.");
        return activeTerminalIds.Count == 1 ? activeTerminalIds[0] : null;
    }

    public async Task<decimal> ResolveHistoricalUnitCostSnapshotAsync(
        long variantId,
        CancellationToken cancellationToken)
    {
        var completedReceiptLines = await dbContext.GrnItems
            .Where(item => item.Grn.Status == "completed" &&
                           item.PurchaseOrderItem.VariantId == variantId
                && item.ReceivedQty > 0)
            .Select(item => new { item.ReceivedQty, item.UnitCost })
            .ToListAsync(cancellationToken);

        if (completedReceiptLines.Count == 0)
        {
            var variant = await dbContext.ProductVariants
                .AsNoTracking()
                .Include(item => item.Product)
                .FirstOrDefaultAsync(item => item.Id == variantId, cancellationToken);
            if (variant is null)
                throw new InvalidOperationException(
                    $"Variant {variantId} was not found while resolving historical unit cost.");
            return variant.Product.CostPrice + variant.CostAdjustment;
        }

        var totalReceivedQty = completedReceiptLines.Sum(item => item.ReceivedQty);
        if (totalReceivedQty <= 0)
            return 0m;

        var weightedAverageCost = completedReceiptLines.Sum(item => item.ReceivedQty * item.UnitCost) / totalReceivedQty;
        // A base unit can be a millilitre or a gram, where four decimals would lose real money. Plain pcs items keep four.
        var isConfigured = await dbContext.VariantUomConfigs.AnyAsync(config => config.VariantId == variantId, cancellationToken);
        return Math.Round(weightedAverageCost, isConfigured ? 8 : 4);
    }

    public Task<Sale?> FindSaleForUpdateAsync(long saleId, CancellationToken cancellationToken) =>
        dbContext.Sales.FirstOrDefaultAsync(sale => sale.Id == saleId, cancellationToken);

    public async Task<Customer?> FindCustomerForUpdateAsync(long customerId, CancellationToken cancellationToken) =>
        await dbContext.Customers.FindAsync([customerId], cancellationToken);

    public async Task AddSaleAsync(Sale sale, CancellationToken cancellationToken) =>
        await dbContext.Sales.AddAsync(sale, cancellationToken);

    public Task<SaleReadModel?> FindCreatedSaleAsync(long saleId, CancellationToken cancellationToken) =>
        saleReadStore.FindByIdAsync(saleId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task SaveFinalChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SaleInventoryConcurrencyException(exception);
        }
    }

    private sealed class SaleCreationTransaction(IDbContextTransaction transaction) : ISaleCreationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) => transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
