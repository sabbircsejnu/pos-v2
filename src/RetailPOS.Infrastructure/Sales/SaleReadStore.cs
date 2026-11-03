using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleReadStore(RetailPOSDbContext dbContext) : ISaleReadStore
{
    private static SaleReadModel ToReadModel(RetailPOS.Core.Entities.Sale sale) =>
        new(
            sale.Id,
            sale.SaleNumber,
            sale.OutletId,
            sale.Outlet?.Name,
            sale.Outlet?.Address,
            sale.Outlet?.ContactNumber,
            sale.TerminalId,
            sale.Terminal?.Name,
            sale.CashSessionId,
            sale.BusinessDate,
            sale.SaleDateTime,
            sale.CustomerId,
            sale.Customer?.Name,
            sale.CashierId,
            sale.Cashier?.Name,
            sale.SaleDate,
            sale.TotalAmount,
            sale.Discount,
            sale.Tax,
            sale.TotalAmount - sale.Discount + sale.Tax,
            sale.PaymentMethod,
            sale.Status,
            sale.CreatedAt,
            sale.Items?.Select(line => new SaleItemReadModel(
                line.Id,
                line.VariantId,
                line.Variant?.Product?.Name,
                line.Variant?.Sku ?? string.Empty,
                line.Quantity,
                line.UnitPrice,
                line.HistoricalUnitCost,
                line.DiscountAmount,
                line.Subtotal,
                line.AppliedRuleName,
                line.Variant?.Name,
                line.VatRate,
                line.VatAmount,
                line.TaxInclusive ?? false)).ToArray() ?? [],
            sale.Payments?.Select(payment => new SalePaymentReadModel(
                payment.Id,
                payment.Method,
                payment.Amount,
                payment.Tendered,
                payment.ChangeGiven ?? (payment.Method == "cash" && payment.Tendered.HasValue
                    ? Math.Max(0, payment.Tendered.Value - payment.Amount)
                    : 0m))).ToArray() ?? [],
            sale.SalespersonUserId,
            sale.Salesperson?.Name);

    public async Task<SaleReadModel?> FindByIdAsync(
        long saleId,
        CancellationToken cancellationToken)
        => await FindByIdAsync(saleId, includeTerminal: false, cancellationToken);

    public async Task<SaleReadModel?> FindForReceiptAsync(
        long saleId,
        CancellationToken cancellationToken)
        => await FindByIdAsync(saleId, includeTerminal: true, cancellationToken);

    private async Task<SaleReadModel?> FindByIdAsync(
        long saleId,
        bool includeTerminal,
        CancellationToken cancellationToken)
    {
        IQueryable<RetailPOS.Core.Entities.Sale> query = dbContext.Sales
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Outlet)
            .Include(item => item.Customer)
            .Include(item => item.Cashier)
            .Include(item => item.Salesperson)
            .Include(item => item.Items)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
            .Include(item => item.Payments);

        if (includeTerminal)
        {
            query = query.Include(item => item.Terminal);
        }

        var sale = await query.FirstOrDefaultAsync(item => item.Id == saleId, cancellationToken);

        if (sale is null)
        {
            return null;
        }

        return ToReadModel(sale);
    }

    public async Task<SaleSearchReadModel> SearchAsync(
        SaleSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Sales
            .AsNoTracking()
            .AsSplitQuery()
            .Include(sale => sale.Outlet)
            .Include(sale => sale.Customer)
            .Include(sale => sale.Cashier)
            .Include(sale => sale.Items)
            .AsQueryable();

        if (criteria.OutletId.HasValue)
        {
            query = query.Where(sale => sale.OutletId == criteria.OutletId.Value);
        }

        if (criteria.CashierId.HasValue)
        {
            query = query.Where(sale => sale.CashierId == criteria.CashierId.Value);
        }

        if (criteria.CustomerId.HasValue)
        {
            query = query.Where(sale => sale.CustomerId == criteria.CustomerId.Value);
        }

        if (criteria.StartDate.HasValue)
        {
            query = query.Where(sale => sale.SaleDate >= criteria.StartDate.Value);
        }

        if (criteria.EndDate.HasValue)
        {
            query = query.Where(sale => sale.SaleDate <= criteria.EndDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Status))
        {
            var normalizedStatus = criteria.Status.ToLower();
            query = query.Where(sale => sale.Status.ToLower() == normalizedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var sales = await query
            .OrderByDescending(sale => sale.SaleDate)
            .Skip((criteria.PageNumber - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new SaleSearchReadModel(
            sales.Select(ToReadModel).ToArray(),
            totalCount,
            criteria.PageNumber,
            criteria.PageSize);
    }
}
