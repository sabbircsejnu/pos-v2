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

    public async Task<bool> TryMarkSaleRefundedAsync(
        long saleId,
        string expectedStatus,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var currentStatus = await FindCurrentSaleStatusAsync(saleId, cancellationToken);
            return string.Equals(currentStatus, expectedStatus, StringComparison.OrdinalIgnoreCase);
        }

        var normalizedStatus = expectedStatus.ToLower();
        return await dbContext.Sales
            .Where(sale => sale.Id == saleId && sale.Status.ToLower() == normalizedStatus)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(sale => sale.Status, "refunded"),
                cancellationToken) == 1;
    }

    public Task<Inventory?> FindOutletInventoryAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken) =>
        dbContext.Inventories.FirstOrDefaultAsync(
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
