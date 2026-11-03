using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleTodaySummaryReadStore(RetailPOSDbContext dbContext)
    : ISaleTodaySummaryReadStore
{
    public async Task<IReadOnlyList<SaleSummaryLineReadModel>> GetAsync(
        long? outletId,
        DateTime startUtc,
        DateTime endExclusiveUtc,
        CancellationToken cancellationToken)
    {
        var sales = dbContext.Sales
            .AsNoTracking()
            .Where(sale =>
                sale.SaleDate >= startUtc &&
                sale.SaleDate < endExclusiveUtc &&
                sale.Status != "voided");

        if (outletId.HasValue)
        {
            sales = sales.Where(sale => sale.OutletId == outletId.Value);
        }

        return await sales
            .Select(sale => new SaleSummaryLineReadModel(
                sale.PaymentMethod,
                sale.TotalAmount,
                sale.Discount,
                sale.Tax))
            .ToListAsync(cancellationToken);
    }
}
