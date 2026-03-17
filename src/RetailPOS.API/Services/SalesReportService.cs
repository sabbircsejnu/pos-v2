using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

/// <summary>
/// Provides aggregated sales report data directly from the database
/// </summary>
public class SalesReportService : ISalesReportService
{
    private readonly RetailPOSDbContext _context;

    public SalesReportService(RetailPOSDbContext context)
    {
        _context = context;
    }

    /// <summary>Returns aggregated sales summary for the given filter</summary>
    public async Task<SalesReportDto> GetSummaryAsync(SalesReportFilterDto filter)
    {
        var (start, end) = ResolveRange(filter.StartDate, filter.EndDate);

        var query = _context.Sales
            .AsNoTracking()
            .Where(s => s.SaleDate >= start && s.SaleDate <= end);

        if (filter.OutletId.HasValue)
            query = query.Where(s => s.OutletId == filter.OutletId.Value);

        if (!string.IsNullOrWhiteSpace(filter.PaymentMethod))
            query = query.Where(s => s.PaymentMethod == filter.PaymentMethod);

        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalRevenue    = g.Sum(s => s.TotalAmount),
                TotalDiscount   = g.Sum(s => s.Discount),
                TotalTax        = g.Sum(s => s.Tax),
                TotalTransactions = g.Count()
            })
            .FirstOrDefaultAsync();

        if (totals == null)
            return new SalesReportDto();

        var netRevenue = totals.TotalRevenue - totals.TotalDiscount;
        return new SalesReportDto
        {
            TotalRevenue      = totals.TotalRevenue,
            TotalDiscount     = totals.TotalDiscount,
            TotalTax          = totals.TotalTax,
            NetRevenue        = netRevenue,
            TotalTransactions = totals.TotalTransactions,
            AverageOrderValue = totals.TotalTransactions > 0
                ? netRevenue / totals.TotalTransactions
                : 0
        };
    }

    /// <summary>Returns top-selling product variants by quantity</summary>
    public async Task<List<TopProductDto>> GetTopProductsAsync(SalesReportFilterDto filter, int limit = 10)
    {
        var (start, end) = ResolveRange(filter.StartDate, filter.EndDate);

        var query = _context.SaleItems
            .AsNoTracking()
            .Where(si => si.Sale.SaleDate >= start && si.Sale.SaleDate <= end);

        if (filter.OutletId.HasValue)
            query = query.Where(si => si.Sale.OutletId == filter.OutletId.Value);

        var grouped = await query
            .GroupBy(si => si.VariantId)
            .Select(g => new
            {
                VariantId     = g.Key,
                TotalQuantity = g.Sum(si => si.Quantity),
                TotalRevenue  = g.Sum(si => si.Subtotal)
            })
            .OrderByDescending(x => x.TotalQuantity)
            .Take(limit)
            .ToListAsync();

        var variantIds = grouped.Select(x => x.VariantId).ToList();
        var variantMap = await _context.ProductVariants
            .AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku, ProductName = v.Product.Name })
            .ToDictionaryAsync(v => v.Id);

        return grouped.Select(g => new TopProductDto
        {
            VariantId     = g.VariantId,
            ProductName   = variantMap.TryGetValue(g.VariantId, out var variant) ? variant.ProductName : string.Empty,
            Sku           = variantMap.TryGetValue(g.VariantId, out var skuVariant) ? skuVariant.Sku : string.Empty,
            TotalQuantity = g.TotalQuantity,
            TotalRevenue  = g.TotalRevenue
        }).ToList();
    }

    /// <summary>Returns sales totals grouped by outlet</summary>
    public async Task<List<SalesByOutletDto>> GetSalesByOutletAsync(SalesReportFilterDto filter)
    {
        var (start, end) = ResolveRange(filter.StartDate, filter.EndDate);

        var grouped = await _context.Sales
            .AsNoTracking()
            .Where(s => s.SaleDate >= start && s.SaleDate <= end)
            .GroupBy(s => s.OutletId)
            .Select(g => new
            {
                OutletId         = g.Key,
                TransactionCount = g.Count(),
                TotalRevenue     = g.Sum(s => s.TotalAmount)
            })
            .ToListAsync();

        var outletIds = grouped.Select(x => x.OutletId).ToList();
        var outletNames = await _context.Outlets
            .AsNoTracking()
            .Where(o => outletIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name);

        return grouped
            .Select(g => new SalesByOutletDto
            {
                OutletId         = g.OutletId,
                OutletName       = outletNames.TryGetValue(g.OutletId, out var n) ? n : string.Empty,
                TransactionCount = g.TransactionCount,
                TotalRevenue     = g.TotalRevenue
            })
            .OrderByDescending(x => x.TotalRevenue)
            .ToList();
    }

    /// <summary>Returns sales totals grouped by payment method</summary>
    public async Task<List<SalesByPaymentMethodDto>> GetSalesByPaymentMethodAsync(SalesReportFilterDto filter)
    {
        var (start, end) = ResolveRange(filter.StartDate, filter.EndDate);

        var query = _context.Sales
            .AsNoTracking()
            .Where(s => s.SaleDate >= start && s.SaleDate <= end);

        if (filter.OutletId.HasValue)
            query = query.Where(s => s.OutletId == filter.OutletId.Value);

        return await query
            .GroupBy(s => s.PaymentMethod)
            .Select(g => new SalesByPaymentMethodDto
            {
                PaymentMethod = g.Key,
                Count         = g.Count(),
                Amount        = g.Sum(s => s.TotalAmount)
            })
            .OrderByDescending(x => x.Amount)
            .ToListAsync();
    }

    /// <summary>Returns daily revenue and transaction count for the period</summary>
    public async Task<List<DailySalesTrendDto>> GetDailySalesTrendAsync(SalesReportFilterDto filter)
    {
        var (start, end) = ResolveRange(filter.StartDate, filter.EndDate);

        var query = _context.Sales
            .AsNoTracking()
            .Where(s => s.SaleDate >= start && s.SaleDate <= end);

        if (filter.OutletId.HasValue)
            query = query.Where(s => s.OutletId == filter.OutletId.Value);

        return await query
            .GroupBy(s => s.SaleDate.Date)
            .Select(g => new DailySalesTrendDto
            {
                Date             = g.Key,
                TransactionCount = g.Count(),
                Revenue          = g.Sum(s => s.TotalAmount)
            })
            .OrderBy(x => x.Date)
            .ToListAsync();
    }

    private static (DateTime start, DateTime end) ResolveRange(DateTime? startDate, DateTime? endDate)
    {
        var end   = endDate?.ToUniversalTime()   ?? DateTime.UtcNow;
        var start = startDate?.ToUniversalTime() ?? end.AddDays(-30);
        return (start, end);
    }
}
