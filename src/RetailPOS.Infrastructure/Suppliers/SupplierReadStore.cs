using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Suppliers;
using RetailPOS.Application.Suppliers.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Infrastructure.Suppliers;

public sealed class SupplierReadStore(RetailPOSDbContext dbContext)
    : ISupplierReadStore
{
    public async Task<IReadOnlyList<SupplierReadModel>> GetAllAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        await dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.BusinessId == businessId)
            .OrderBy(supplier => supplier.Name)
            .Select(ReadProjection)
            .ToListAsync(cancellationToken);

    public async Task<SupplierSearchReadModel> SearchAsync(
        string? searchQuery,
        decimal? minCreditLimit,
        decimal? maxCreditLimit,
        int pageNumber,
        int pageSize,
        string sortBy,
        string sortOrder,
        long businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.ToLower();
            query = query.Where(supplier =>
                supplier.Name.ToLower().Contains(search)
                || (supplier.Contact != null && supplier.Contact.ToLower().Contains(search))
                || (supplier.Address != null && supplier.Address.ToLower().Contains(search)));
        }

        if (minCreditLimit.HasValue)
            query = query.Where(supplier => supplier.CreditLimit >= minCreditLimit.Value);
        if (maxCreditLimit.HasValue)
            query = query.Where(supplier => supplier.CreditLimit <= maxCreditLimit.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(supplier => supplier.Name)
                : query.OrderBy(supplier => supplier.Name),
            "creditlimit" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(supplier => supplier.CreditLimit)
                : query.OrderBy(supplier => supplier.CreditLimit),
            "createdat" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(supplier => supplier.CreatedAt)
                : query.OrderBy(supplier => supplier.CreatedAt),
            _ => query.OrderBy(supplier => supplier.Name)
        };

        var suppliers = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ReadProjection)
            .ToListAsync(cancellationToken);
        return new SupplierSearchReadModel(suppliers, totalCount);
    }

    public Task<SupplierReadModel?> GetByIdAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.Id == supplierId && supplier.BusinessId == businessId)
            .Select(ReadProjection)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SupplierPerformanceDataReadModel?> GetPerformanceDataAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers
            .AsNoTracking()
            .Where(candidate => candidate.Id == supplierId && candidate.BusinessId == businessId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.CreditLimit
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (supplier is null)
            return null;

        var purchaseOrders = dbContext.PurchaseOrders
            .Where(order => order.SupplierId == supplierId)
            .WhereBusiness(businessId);
        var totalPurchaseOrders = await purchaseOrders.CountAsync(cancellationToken);
        var totalPurchaseAmount = await purchaseOrders.SumAsync(order => order.TotalAmount, cancellationToken);
        var totalBills = await dbContext.Bills
            .CountAsync(
                bill => bill.SupplierId == supplierId && bill.Supplier.BusinessId == businessId,
                cancellationToken);
        var outstandingBalance = await dbContext.Bills
            .Where(bill =>
                bill.SupplierId == supplierId
                && bill.Supplier.BusinessId == businessId
                && bill.Status != "paid")
            .SumAsync(bill => bill.AmountDue, cancellationToken);

        return new SupplierPerformanceDataReadModel(
            supplier.Id,
            supplier.Name,
            totalPurchaseOrders,
            totalBills,
            totalPurchaseAmount,
            outstandingBalance,
            supplier.CreditLimit);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Supplier, SupplierReadModel>> ReadProjection =
        supplier => new SupplierReadModel(
            supplier.Id,
            supplier.Name,
            supplier.Contact,
            supplier.Address,
            supplier.CreditLimit,
            supplier.CreatedAt,
            supplier.UpdatedAt);
}
