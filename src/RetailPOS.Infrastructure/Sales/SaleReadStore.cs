using RetailPOS.Application.Settings.Abstractions;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleReadStore(
    RetailPOSDbContext dbContext,
    ICompanyTimeZoneProvider timeZones) : ISaleReadStore
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
                line.TaxInclusive ?? false,
                line.UomUnitCode,
                line.UomLabel,
                line.UomEnteredQuantity,
                line.UomEnteredUnitPrice,
                line.UomBaseUnitCode,
                line.PackState,
                line.PackagingCost,
                line.ReturnedQuantity,
                // A sale closed as refunded or voided has nothing to return, even if its old refund was partial.
                sale.Status.ToLowerInvariant() is "refunded" or "voided" ? 0m : line.Quantity - line.ReturnedQuantity)).ToArray() ?? [],
            sale.Payments?.Select(payment => new SalePaymentReadModel(
                payment.Id,
                payment.Method,
                payment.Amount,
                payment.Tendered,
                payment.ChangeGiven ?? (payment.Method == "cash" && payment.Tendered.HasValue
                    ? Math.Max(0, payment.Tendered.Value - payment.Amount)
                    : 0m))).ToArray() ?? [],
            sale.SalespersonUserId,
            sale.Salesperson?.Name)
        {
            Refunds = sale.Refunds?
                .OrderBy(refund => refund.Sequence)
                .Select(refund => new SaleRefundReadModel(
                    refund.Id,
                    refund.RefundNumber,
                    refund.Sequence,
                    refund.CreatedAtUtc,
                    refund.Reason,
                    refund.ActorUserId,
                    refund.Actor?.Name,
                    refund.NetAmount,
                    refund.VatAmount,
                    refund.TotalAmount,
                    refund.CashAmount,
                    refund.Lines.Select(line => new SaleRefundLineReadModel(
                        line.SaleItemId,
                        line.ReturnedQuantity,
                        line.Condition,
                        line.RestockedQuantity,
                        line.PackagingReusable,
                        line.NetAmount,
                        line.VatAmount)).ToArray(),
                    refund.Payments.Select(payment => new SaleRefundPaymentReadModel(payment.Method, payment.Amount)).ToArray()))
                .ToArray() ?? []
        };

    public async Task<SaleReadModel?> FindByIdAsync(
        long saleId,
        CancellationToken cancellationToken)
        => await FindByIdAsync(saleId, includeTerminal: false, cancellationToken);

    public async Task<SaleReadModel?> FindForReceiptAsync(
        long saleId,
        CancellationToken cancellationToken)
    {
        var entity = await LoadByIdAsync(saleId, includeTerminal: true, cancellationToken);
        if (entity is null)
            return null;

        var businessId = entity.BusinessId ?? entity.Outlet?.BusinessId;
        var businessZone = businessId.HasValue
            ? await timeZones.GetTimeZoneIdAsync(businessId.Value, cancellationToken)
            : BusinessPosSettings.DefaultTimeZone;

        var poweredBy = businessId.HasValue
            ? await dbContext.Businesses
                .AsNoTracking()
                .Where(business => business.Id == businessId.Value)
                .Select(business => new { business.ShowPoweredByOnReceipt, business.PoweredByText })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return ToReadModel(entity) with
        {
            CustomerPhone = entity.Customer?.Phone,
            OutletPrint = entity.Outlet is null
                ? null
                : new SaleOutletPrintReadModel(
                    entity.Outlet.InvoicePrintBranchName,
                    entity.Outlet.BinNumber,
                    entity.Outlet.Mushak,
                    entity.Outlet.ReturnExchangePolicy,
                    entity.Outlet.VatLawNotification),
            EffectiveTimeZoneId = businessZone,
            ShowPoweredByOnReceipt = poweredBy?.ShowPoweredByOnReceipt ?? false,
            PoweredByText = poweredBy?.PoweredByText
        };
    }

    private async Task<SaleReadModel?> FindByIdAsync(
        long saleId,
        bool includeTerminal,
        CancellationToken cancellationToken)
    {
        var sale = await LoadByIdAsync(saleId, includeTerminal, cancellationToken);
        return sale is null ? null : ToReadModel(sale);
    }

    private async Task<RetailPOS.Core.Entities.Sale?> LoadByIdAsync(
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
            .Include(item => item.Payments)
            .Include(item => item.Refunds)
                .ThenInclude(refund => refund.Lines)
            .Include(item => item.Refunds)
                .ThenInclude(refund => refund.Payments)
            .Include(item => item.Refunds)
                .ThenInclude(refund => refund.Actor);

        if (includeTerminal)
        {
            query = query.Include(item => item.Terminal);
        }

        return await query.FirstOrDefaultAsync(item => item.Id == saleId, cancellationToken);
    }

    public async Task<SaleSearchReadModel> SearchAsync(
        SaleSearchCriteria criteria,
        SaleReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Sales
            .AsNoTracking()
            .AsSplitQuery()
            .Include(sale => sale.Outlet)
            .Include(sale => sale.Customer)
            .Include(sale => sale.Cashier)
            .Include(sale => sale.Items)
            .WhereReadScope(scope);

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
