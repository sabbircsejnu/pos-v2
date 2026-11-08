using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Exchanges;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Core.Entities.Exchanges;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Exchanges;

/// <summary>
/// Exchange history projections (LLD §21): no tracking, business plus readable receiving outlets on every query, bounded
/// pages ordered by completion time. Names are resolved in batched lookups, never per row. EF Contains uses lists.
/// </summary>
public sealed class ExchangeReadStore(RetailPOSDbContext dbContext) : IExchangeReadStore
{
    public async Task<ExchangeHistoryPageReadModel> SearchAsync(
        long businessId, IReadOnlyList<long> outletIds, string? search, DateOnly? from, DateOnly? to, string? status,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = Scoped(businessId, outletIds);
        if (from is { } start)
        {
            var fromDate = start.ToDateTime(TimeOnly.MinValue);
            query = query.Where(exchange => exchange.BusinessDate >= fromDate);
        }

        if (to is { } end)
        {
            var toDate = end.ToDateTime(TimeOnly.MinValue);
            query = query.Where(exchange => exchange.BusinessDate <= toDate);
        }

        if (status is not null)
            query = query.Where(exchange => exchange.Status == status);
        if (search is not null)
        {
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(exchange =>
                EF.Functions.ILike(exchange.ExchangeNumber, pattern, "\\")
                || dbContext.ExchangeSourceInvoices.Any(invoice => invoice.ExchangeId == exchange.Id
                    && EF.Functions.ILike(invoice.InvoiceNumberSnapshot, pattern, "\\"))
                || dbContext.Customers.Any(customer => customer.Id == exchange.CustomerId
                    && EF.Functions.ILike(customer.Name, pattern, "\\")));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(exchange => exchange.CompletedAtUtc).ThenByDescending(exchange => exchange.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new ExchangeHistoryPageReadModel(await ToItemsAsync(rows, cancellationToken), total, page, pageSize);
    }

    public Task<bool> SaleVisibleAsync(long businessId, IReadOnlyList<long> outletIds, long saleId, CancellationToken cancellationToken)
    {
        var outlets = outletIds.Distinct().ToList();
        return dbContext.Sales.AsNoTracking().AnyAsync(
            sale => sale.Id == saleId && sale.BusinessId == businessId && outlets.Contains(sale.OutletId), cancellationToken);
    }

    public async Task<IReadOnlyList<ExchangeHistoryItemReadModel>> ListBySaleAsync(
        long businessId, IReadOnlyList<long> outletIds, long saleId, CancellationToken cancellationToken)
    {
        var rows = await Scoped(businessId, outletIds)
            .Where(exchange => dbContext.ExchangeSourceInvoices.Any(invoice => invoice.ExchangeId == exchange.Id && invoice.SaleId == saleId))
            .OrderByDescending(exchange => exchange.CompletedAtUtc).ThenByDescending(exchange => exchange.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
        return await ToItemsAsync(rows, cancellationToken);
    }

    public async Task<ExchangeDetailReadModel?> GetDetailAsync(long businessId, IReadOnlyList<long> outletIds, long exchangeId, CancellationToken cancellationToken)
    {
        var exchange = await Scoped(businessId, outletIds)
            .Include(row => row.SourceInvoices)
            .Include(row => row.ReturnLines)
            .Include(row => row.ReplacementLines)
            .Include(row => row.Settlements)
            .AsSplitQuery()
            .FirstOrDefaultAsync(row => row.Id == exchangeId, cancellationToken);
        if (exchange is null)
            return null;

        var approvals = await dbContext.ExchangeApprovals.AsNoTracking()
            .Where(approval => approval.BusinessId == businessId && approval.ConsumedByExchangeId == exchange.Id)
            .OrderBy(approval => approval.Id)
            .ToListAsync(cancellationToken);
        var reversal = exchange.ReversalId is { } reversalId
            ? await dbContext.ExchangeReversals.AsNoTracking().FirstOrDefaultAsync(row => row.Id == reversalId && row.BusinessId == businessId, cancellationToken)
            : null;

        var userIds = new List<long> { exchange.CashierUserId };
        if (exchange.SalespersonUserId is { } salesperson)
            userIds.Add(salesperson);
        userIds.AddRange(approvals.Select(approval => approval.SupervisorUserId));
        if (reversal is not null)
            userIds.Add(reversal.ActorUserId);
        var users = await UserNamesAsync(userIds, cancellationToken);
        var outletName = await dbContext.Outlets.AsNoTracking().Where(outlet => outlet.Id == exchange.ReceivingOutletId).Select(outlet => outlet.Name).FirstOrDefaultAsync(cancellationToken);
        var customerName = await dbContext.Customers.AsNoTracking().Where(customer => customer.Id == exchange.CustomerId).Select(customer => customer.Name).FirstOrDefaultAsync(cancellationToken);
        var variantIds = exchange.ReturnLines.Select(line => line.VariantId).Concat(exchange.ReplacementLines.Select(line => line.VariantId)).Distinct().ToList();
        var variants = await dbContext.ProductVariants.AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && variantIds.Contains(variant.Id))
            .Select(variant => new { variant.Id, ProductName = variant.Product!.Name, variant.Name, variant.Sku })
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);
        var invoices = exchange.SourceInvoices.ToDictionary(invoice => invoice.SaleId, invoice => invoice.InvoiceNumberSnapshot);

        return new ExchangeDetailReadModel(
            exchange.Id,
            exchange.ExchangeNumber,
            Date(exchange.BusinessDate),
            Date(exchange.EligibilityLocalDate),
            exchange.CompletedAtUtc,
            exchange.Status,
            exchange.ReceivingOutletId,
            outletName,
            users.GetValueOrDefault(exchange.CashierUserId),
            exchange.SalespersonUserId is { } spId ? users.GetValueOrDefault(spId) : null,
            exchange.CustomerId,
            customerName,
            exchange.ReasonCode,
            exchange.ReasonOtherText,
            exchange.InternalNote,
            exchange.PhoneVerificationResult,
            exchange.LoyaltyStatus,
            exchange.JournalEntryId,
            exchange.ReturnGross,
            exchange.ReturnVat,
            exchange.ReplacementGross,
            exchange.ReplacementVat,
            exchange.ReplacementDiscount,
            exchange.NetAdjustmentGross,
            exchange.SettlementDirection,
            exchange.SettlementAmount,
            exchange.HighValueAmount,
            exchange.SourceInvoices.OrderBy(invoice => invoice.Id)
                .Select(invoice => new ExchangeOriginalInvoiceReadModel(invoice.SaleId, invoice.InvoiceNumberSnapshot, Date(invoice.PurchaseLocalDate))).ToList(),
            exchange.ReturnLines.OrderBy(line => line.Id).Select(line =>
            {
                var variant = variants.GetValueOrDefault(line.VariantId);
                return new ExchangeReturnLineReadModel(
                    line.Id, line.SourceKind, line.SaleItemId, line.SourceReplacementLineId,
                    line.SaleId is { } saleId ? invoices.GetValueOrDefault(saleId) : null,
                    line.VariantId, variant?.ProductName, variant?.Name, variant?.Sku, line.ReturnedQuantity, line.UomLabel,
                    line.CreditNet, line.CreditVat, line.CreditGross);
            }).ToList(),
            exchange.ReplacementLines.OrderBy(line => line.Id).Select(line =>
            {
                var variant = variants.GetValueOrDefault(line.VariantId);
                return new ExchangeReplacementLineReadModel(
                    line.Id, line.VariantId, variant?.ProductName, variant?.Name, variant?.Sku, line.IssuedQuantity, line.UomLabel,
                    line.UnitPrice, line.LineDiscount, line.InvoiceDiscountShare, line.TaxableAmount, line.VatAmount,
                    line.TaxableAmount + line.VatAmount, line.ReturnedQuantity);
            }).ToList(),
            exchange.Settlements.OrderBy(settlement => settlement.Id).Select(settlement => new ExchangeSettlementReadModel(
                settlement.Direction, settlement.Method, settlement.Amount, settlement.Tendered, settlement.ChangeGiven,
                settlement.ExternalReference is { Length: > 4 } reference ? reference[^4..] : settlement.ExternalReference,
                settlement.ExchangeReversalId is not null)).ToList(),
            approvals.Select(approval => new ExchangeApprovalReadModel(
                approval.ApprovalType, users.GetValueOrDefault(approval.SupervisorUserId), approval.Reason, approval.CreatedAtUtc)).ToList(),
            reversal is null
                ? null
                : new ExchangeReversalSummaryReadModel(reversal.Id, reversal.ReversalNumber, Date(reversal.BusinessDate), reversal.Reason,
                    users.GetValueOrDefault(reversal.ActorUserId), reversal.CompletedAtUtc));
    }

    public Task<ExchangeVisibility?> FindVisibleAsync(long businessId, IReadOnlyList<long> outletIds, long exchangeId, CancellationToken cancellationToken) =>
        Scoped(businessId, outletIds)
            .Where(exchange => exchange.Id == exchangeId)
            .Select(exchange => new ExchangeVisibility(exchange.Id, exchange.ReceivingOutletId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<(string Json, string? BusinessName, string? OutletName, string? CashierName, string? SalespersonName, string? CustomerName)?> GetReceiptAsync(
        long businessId, IReadOnlyList<long> outletIds, long exchangeId, CancellationToken cancellationToken)
    {
        var exchange = await Scoped(businessId, outletIds)
            .Where(row => row.Id == exchangeId)
            .Select(row => new { row.Id, row.ReceivingOutletId, row.CashierUserId, row.SalespersonUserId, row.CustomerId })
            .FirstOrDefaultAsync(cancellationToken);
        if (exchange is null)
            return null;
        var json = await dbContext.ExchangeReceiptSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.ExchangeId == exchange.Id && snapshot.ExchangeReversalId == null)
            .Select(snapshot => snapshot.Document)
            .FirstOrDefaultAsync(cancellationToken);
        if (json is null)
            return null;

        var users = await UserNamesAsync(exchange.SalespersonUserId is { } sp ? [exchange.CashierUserId, sp] : [exchange.CashierUserId], cancellationToken);
        var businessName = await dbContext.Businesses.AsNoTracking().Where(business => business.Id == businessId).Select(business => business.Name).FirstOrDefaultAsync(cancellationToken);
        var outletName = await dbContext.Outlets.AsNoTracking().Where(outlet => outlet.Id == exchange.ReceivingOutletId).Select(outlet => outlet.Name).FirstOrDefaultAsync(cancellationToken);
        var customerName = await dbContext.Customers.AsNoTracking().Where(customer => customer.Id == exchange.CustomerId).Select(customer => customer.Name).FirstOrDefaultAsync(cancellationToken);
        return (json, businessName, outletName, users.GetValueOrDefault(exchange.CashierUserId),
            exchange.SalespersonUserId is { } id ? users.GetValueOrDefault(id) : null, customerName);
    }

    public async Task<IReadOnlyDictionary<long, string>> GetVariantNamesAsync(long businessId, IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken)
    {
        var ids = variantIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<long, string>();
        var rows = await dbContext.ProductVariants.AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && ids.Contains(variant.Id))
            .Select(variant => new { variant.Id, ProductName = variant.Product!.Name, variant.Name })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => string.IsNullOrWhiteSpace(row.Name) ? row.ProductName : $"{row.ProductName} - {row.Name}");
    }

    public Task<bool> IsOutletTerminalAsync(long outletId, long terminalId, CancellationToken cancellationToken) =>
        dbContext.PosTerminals.AsNoTracking().AnyAsync(terminal => terminal.Id == terminalId && terminal.OutletId == outletId, cancellationToken);

    public async Task<ExchangeMetricsReadModel> GetMetricsAsync(
        long businessId, IReadOnlyList<long> outletIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var outlets = outletIds.Distinct().ToList();
        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue);

        // An exchange counts on its own business date whatever its status; its reversal offsets it on the reversal's date.
        var exchanges = await Scoped(businessId, outlets)
            .Where(exchange => exchange.BusinessDate >= fromDate && exchange.BusinessDate <= toDate)
            .GroupBy(exchange => exchange.ReceivingOutletId)
            .Select(group => new
            {
                OutletId = group.Key,
                Count = group.Count(),
                ReplacementGross = group.Sum(exchange => exchange.ReplacementGross),
                ReturnGross = group.Sum(exchange => exchange.ReturnGross),
                NetGross = group.Sum(exchange => exchange.NetAdjustmentGross),
                ReplacementNet = group.Sum(exchange => exchange.ReplacementNet),
                ReturnNet = group.Sum(exchange => exchange.ReturnNet),
                NetNet = group.Sum(exchange => exchange.NetAdjustmentNet)
            })
            .ToListAsync(cancellationToken);
        var reversals = await dbContext.ExchangeReversals.AsNoTracking()
            .Where(reversal => reversal.BusinessId == businessId
                && outlets.Contains(reversal.ReceivingOutletId)
                && reversal.BusinessDate >= fromDate
                && reversal.BusinessDate <= toDate)
            .Join(dbContext.Exchanges.Where(exchange => exchange.BusinessId == businessId),
                reversal => reversal.ExchangeId, exchange => exchange.Id,
                (reversal, exchange) => new { reversal.ReceivingOutletId, Exchange = exchange })
            .GroupBy(row => row.ReceivingOutletId)
            .Select(group => new
            {
                OutletId = group.Key,
                Count = group.Count(),
                ReplacementGross = group.Sum(row => row.Exchange.ReplacementGross),
                ReturnGross = group.Sum(row => row.Exchange.ReturnGross),
                NetGross = group.Sum(row => row.Exchange.NetAdjustmentGross),
                ReplacementNet = group.Sum(row => row.Exchange.ReplacementNet),
                ReturnNet = group.Sum(row => row.Exchange.ReturnNet),
                NetNet = group.Sum(row => row.Exchange.NetAdjustmentNet)
            })
            .ToListAsync(cancellationToken);
        var salespeople = await dbContext.ExchangeSalespersonAdjustments.AsNoTracking()
            .Where(fact => fact.BusinessId == businessId
                && outlets.Contains(fact.ReceivingOutletId)
                && fact.BusinessDate >= fromDate
                && fact.BusinessDate <= toDate)
            .GroupBy(fact => fact.SalespersonUserId)
            .Select(group => new
            {
                SalespersonUserId = group.Key,
                Positive = group.Sum(fact => fact.PositiveAmountNet),
                Negative = group.Sum(fact => fact.NegativeAmountNet),
                Signed = group.Sum(fact => fact.SignedAmountNet),
                TargetEffect = group.Sum(fact => fact.TargetEffectAmountNet)
            })
            .ToListAsync(cancellationToken);

        var outletIdsInResult = exchanges.Select(row => row.OutletId).Concat(reversals.Select(row => row.OutletId)).Distinct().ToList();
        var outletNames = await dbContext.Outlets.AsNoTracking().Where(outlet => outletIdsInResult.Contains(outlet.Id))
            .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);
        var users = await UserNamesAsync(salespeople.Where(row => row.SalespersonUserId is not null).Select(row => row.SalespersonUserId!.Value).ToList(), cancellationToken);

        var byOutlet = outletIdsInResult
            .Select(outletId =>
            {
                var done = exchanges.FirstOrDefault(row => row.OutletId == outletId);
                var undone = reversals.FirstOrDefault(row => row.OutletId == outletId);
                return new ExchangeOutletMetricsReadModel(
                    outletId,
                    outletNames.GetValueOrDefault(outletId),
                    (done?.NetNet ?? 0m) - (undone?.NetNet ?? 0m),
                    (done?.NetGross ?? 0m) - (undone?.NetGross ?? 0m),
                    done?.Count ?? 0);
            })
            .OrderBy(row => row.OutletName).ThenBy(row => row.OutletId)
            .ToList();

        return new ExchangeMetricsReadModel(
            Date(fromDate),
            Date(toDate),
            exchanges.Sum(row => row.Count),
            reversals.Sum(row => row.Count),
            exchanges.Sum(row => row.ReplacementGross) - reversals.Sum(row => row.ReplacementGross),
            exchanges.Sum(row => row.ReturnGross) - reversals.Sum(row => row.ReturnGross),
            exchanges.Sum(row => row.NetGross) - reversals.Sum(row => row.NetGross),
            exchanges.Sum(row => row.ReplacementNet) - reversals.Sum(row => row.ReplacementNet),
            exchanges.Sum(row => row.ReturnNet) - reversals.Sum(row => row.ReturnNet),
            exchanges.Sum(row => row.NetNet) - reversals.Sum(row => row.NetNet),
            byOutlet,
            salespeople
                .Select(row => new ExchangeSalespersonMetricsReadModel(
                    row.SalespersonUserId,
                    row.SalespersonUserId is { } id ? users.GetValueOrDefault(id) : null,
                    row.Positive, row.Negative, row.Signed, row.TargetEffect))
                .OrderByDescending(row => row.SignedNet).ThenBy(row => row.SalespersonUserId)
                .ToList());
    }

    public Task<bool> IsReversedExchangeNumberAsync(long businessId, string exchangeNumber, CancellationToken cancellationToken) =>
        dbContext.Exchanges.AsNoTracking().AnyAsync(row => row.BusinessId == businessId
            && row.ExchangeNumber == exchangeNumber && row.Status == ExchangeStatuses.Reversed, cancellationToken);

    public async Task<ExchangeReExchangeSource?> FindCompletedExchangeByNumberAsync(long businessId, string exchangeNumber, CancellationToken cancellationToken)
    {
        var exchange = await dbContext.Exchanges.AsNoTracking()
            .Include(row => row.ReplacementLines)
            .FirstOrDefaultAsync(row => row.BusinessId == businessId
                && row.ExchangeNumber == exchangeNumber
                && row.Status == ExchangeStatuses.Completed, cancellationToken);
        if (exchange is null)
            return null;

        var outletName = await dbContext.Outlets.AsNoTracking().Where(outlet => outlet.Id == exchange.ReceivingOutletId).Select(outlet => outlet.Name).FirstOrDefaultAsync(cancellationToken);
        var customer = await dbContext.Customers.AsNoTracking()
            .Where(row => row.Id == exchange.CustomerId)
            .Select(row => new { row.Name, row.Phone, row.IsSystem })
            .FirstOrDefaultAsync(cancellationToken);
        var variantIds = exchange.ReplacementLines.Select(line => line.VariantId).Distinct().ToList();
        var variants = await dbContext.ProductVariants.AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && variantIds.Contains(variant.Id))
            .Select(variant => new { variant.Id, ProductName = variant.Product!.Name, variant.Name, variant.Sku, variant.Barcode })
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        return new ExchangeReExchangeSource(
            exchange.Id,
            exchange.ExchangeNumber,
            exchange.EligibilityLocalDate,
            exchange.ReceivingOutletId,
            outletName,
            exchange.CustomerId,
            customer?.Name,
            customer?.Phone,
            customer?.IsSystem ?? false,
            exchange.Status,
            exchange.ReplacementLines.OrderBy(line => line.Id).Select(line =>
            {
                var variant = variants.GetValueOrDefault(line.VariantId);
                return new ExchangeReExchangeSourceLine(
                    line.Id, line.VariantId, variant?.ProductName, variant?.Name, variant?.Sku, variant?.Barcode,
                    line.IssuedQuantity, line.ReturnedQuantity, line.UomLabel, line.UomEnteredQuantity,
                    line.TaxableAmount + line.VatAmount);
            }).ToList());
    }

    private IQueryable<Exchange> Scoped(long businessId, IReadOnlyList<long> outletIds)
    {
        var outlets = outletIds.Distinct().ToList();
        return dbContext.Exchanges.AsNoTracking()
            .Where(exchange => exchange.BusinessId == businessId && outlets.Contains(exchange.ReceivingOutletId));
    }

    private async Task<IReadOnlyList<ExchangeHistoryItemReadModel>> ToItemsAsync(IReadOnlyList<Exchange> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return [];
        var exchangeIds = rows.Select(row => row.Id).ToList();
        var invoices = (await dbContext.ExchangeSourceInvoices.AsNoTracking()
                .Where(invoice => exchangeIds.Contains(invoice.ExchangeId))
                .OrderBy(invoice => invoice.Id)
                .Select(invoice => new { invoice.ExchangeId, invoice.InvoiceNumberSnapshot })
                .ToListAsync(cancellationToken))
            .GroupBy(invoice => invoice.ExchangeId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(invoice => invoice.InvoiceNumberSnapshot).ToList());
        var users = await UserNamesAsync(rows.Select(row => row.CashierUserId)
            .Concat(rows.Where(row => row.SalespersonUserId is not null).Select(row => row.SalespersonUserId!.Value)).ToList(), cancellationToken);
        var outletIds = rows.Select(row => row.ReceivingOutletId).Distinct().ToList();
        var outlets = await dbContext.Outlets.AsNoTracking().Where(outlet => outletIds.Contains(outlet.Id))
            .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);
        var customerIds = rows.Select(row => row.CustomerId).Distinct().ToList();
        var customers = await dbContext.Customers.AsNoTracking().Where(customer => customerIds.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id, customer => customer.Name, cancellationToken);

        return rows.Select(row => new ExchangeHistoryItemReadModel(
            row.Id,
            row.ExchangeNumber,
            Date(row.BusinessDate),
            row.CompletedAtUtc,
            row.ReceivingOutletId,
            outlets.GetValueOrDefault(row.ReceivingOutletId),
            customers.GetValueOrDefault(row.CustomerId),
            invoices.GetValueOrDefault(row.Id) ?? [],
            row.ReasonCode,
            row.NetAdjustmentGross,
            row.SettlementDirection,
            row.SettlementAmount,
            row.Status,
            users.GetValueOrDefault(row.CashierUserId),
            row.SalespersonUserId is { } id ? users.GetValueOrDefault(id) : null)).ToList();
    }

    private async Task<Dictionary<long, string>> UserNamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return await dbContext.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);
    }

    private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
