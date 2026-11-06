using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleTodaySummaryReadStore(RetailPOSDbContext dbContext)
    : ISaleTodaySummaryReadStore
{
    public async Task<IReadOnlyList<SaleSummaryLineReadModel>> GetAsync(
        SaleReadScope scope,
        DateTime startUtc,
        DateTime endExclusiveUtc,
        CancellationToken cancellationToken) =>
        await dbContext.Sales
            .AsNoTracking()
            .WhereReadScope(scope)
            .Where(sale =>
                sale.SaleDate >= startUtc &&
                sale.SaleDate < endExclusiveUtc &&
                sale.Status != "voided")
            .Select(sale => new SaleSummaryLineReadModel(
                sale.PaymentMethod,
                sale.TotalAmount,
                sale.Discount,
                sale.Tax))
            .ToListAsync(cancellationToken);
}
