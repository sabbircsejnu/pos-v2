using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleVoidStore(RetailPOSDbContext dbContext) : ISaleVoidStore, ISaleRefundStore
{
    public async Task<ISaleVoidTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new SaleVoidTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));

    public Task<Sale?> FindSaleForUpdateAsync(long saleId, CancellationToken cancellationToken) =>
        dbContext.Sales
            .Include(sale => sale.Outlet)
            .Include(sale => sale.Terminal)
            .Include(sale => sale.Customer)
            .Include(sale => sale.Cashier)
            .Include(sale => sale.Items)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
            .Include(sale => sale.Items)
                .ThenInclude(item => item.Components)
            .Include(sale => sale.Payments)
            .Include(sale => sale.Refunds)
                .ThenInclude(refund => refund.Payments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(sale => sale.Id == saleId, cancellationToken);

    public Task<string?> FindCurrentSaleStatusAsync(long saleId, CancellationToken cancellationToken) =>
        dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.Id == saleId)
            .Select(sale => sale.Status)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryMarkCompletedSaleVoidedAsync(
        long saleId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var currentStatus = await FindCurrentSaleStatusAsync(saleId, cancellationToken);
            return string.Equals(currentStatus, "completed", StringComparison.OrdinalIgnoreCase);
        }

        return await dbContext.Sales
            .Where(sale => sale.Id == saleId && sale.Status.ToLower() == "completed")
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(sale => sale.Status, "voided"),
                cancellationToken) == 1;
    }

    public async Task LockSaleAsync(long saleId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM sales WHERE id = {saleId} FOR UPDATE",
                cancellationToken);
    }

    public async Task AddRefundAsync(RetailPOS.Core.Entities.Uom.SaleRefund refund, CancellationToken cancellationToken)
    {
        await dbContext.Set<RetailPOS.Core.Entities.Uom.SaleRefund>().AddAsync(refund, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Locked for the rest of the void or refund (after the sale row, the same order everywhere). A concurrent sale of the
    /// same item then waits instead of making this transaction fail on a stale row version.
    /// </summary>
    public Task<Inventory?> FindOutletInventoryAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken) =>
        dbContext.Database.IsNpgsql()
            ? dbContext.Inventories
                .FromSqlInterpolated($"SELECT *, xmin FROM inventories WHERE variant_id = {variantId} AND location_id = {outletId} AND location_type = 'outlet' FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken)
            : dbContext.Inventories.FirstOrDefaultAsync(
                inventory => inventory.VariantId == variantId &&
                             inventory.LocationId == outletId &&
                             inventory.LocationType == "outlet",
                cancellationToken);

    public Task<Customer?> FindCustomerForUpdateAsync(
        long customerId,
        CancellationToken cancellationToken) =>
        dbContext.Customers.FirstOrDefaultAsync(customer => customer.Id == customerId, cancellationToken);

    public async Task AddSaleVoidAsync(SaleVoid saleVoid, CancellationToken cancellationToken) =>
        await dbContext.SaleVoids.AddAsync(saleVoid, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private sealed class SaleVoidTransaction(IDbContextTransaction transaction) : ISaleVoidTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) => transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
