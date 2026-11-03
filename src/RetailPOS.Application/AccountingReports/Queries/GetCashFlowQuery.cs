using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetCashFlowQuery(CashFlowFilter Filter)
    : IQuery<CashFlowReportReadModel>;

public sealed class GetCashFlowQueryHandler(
    ISender sender,
    IAccountingReportTenantScope tenantScope,
    ICashFlowOwnerAccountsProvider ownerAccountsProvider)
    : IRequestHandler<GetCashFlowQuery, CashFlowReportReadModel>
{
    private static readonly string[] LiquidityAccountSlots = ["cash", "petty_cash", "bank"];

    public async Task<CashFlowReportReadModel> Handle(
        GetCashFlowQuery request,
        CancellationToken cancellationToken)
    {
        ValidateLocationType(request.Filter.LocationType);
        var cashBook = await sender.Send(
            new GetCashBookQuery(
                new CashBookFilter(
                    request.Filter.DateFrom,
                    request.Filter.DateTo,
                    request.Filter.LocationId,
                    request.Filter.LocationType,
                    request.Filter.JournalType,
                    request.Filter.ReferenceType,
                    request.Filter.Reference),
                LiquidityAccountSlots),
            cancellationToken);
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var ownerAccounts = await ownerAccountsProvider.GetAsync(businessId, cancellationToken);
        var operating = new List<CashFlowRowReadModel>();
        var investing = new List<CashFlowRowReadModel>();
        var financing = new List<CashFlowRowReadModel>();
        var unclassified = new List<CashFlowRowReadModel>();
        var internalTransfers = new List<CashFlowRowReadModel>();
        var cutoverAndNonCash = new List<CashFlowRowReadModel>();

        foreach (var account in cashBook.Accounts)
        {
            foreach (var row in account.Rows)
            {
                var (activityType, reason) = Classify(
                    row,
                    ownerAccounts.OwnerCapitalAccountId,
                    ownerAccounts.OwnerDrawingsAccountId);
                var flowRow = MapRow(account, row, reason);
                switch (activityType)
                {
                    case "Internal":
                        internalTransfers.Add(flowRow);
                        break;
                    case "CutoverOrNonCash":
                        cutoverAndNonCash.Add(flowRow);
                        break;
                    case "Operating":
                        operating.Add(flowRow);
                        break;
                    case "Investing":
                        investing.Add(flowRow);
                        break;
                    case "Financing":
                        financing.Add(flowRow);
                        break;
                    default:
                        unclassified.Add(flowRow);
                        break;
                }
            }
        }

        var sections = new[]
        {
            BuildSection("Operating", operating),
            BuildSection("Investing", investing),
            BuildSection("Financing", financing),
            BuildSection("Unclassified", unclassified)
        };
        var operatingNet = sections.Single(section => section.ActivityType == "Operating").NetMovement;
        var investingNet = sections.Single(section => section.ActivityType == "Investing").NetMovement;
        var financingNet = sections.Single(section => section.ActivityType == "Financing").NetMovement;
        var unclassifiedNet = sections.Single(section => section.ActivityType == "Unclassified").NetMovement;
        var externalNet = operatingNet + investingNet + financingNet + unclassifiedNet;
        return new CashFlowReportReadModel(
            cashBook.DateFrom,
            cashBook.DateTo,
            "Direct",
            cashBook.Accounts.Sum(account => account.OpeningBalance),
            cashBook.Accounts.Sum(account => account.ClosingBalance),
            operatingNet,
            investingNet,
            financingNet,
            unclassifiedNet,
            externalNet,
            cashBook.Accounts.Sum(account =>
                account.GeneralLedgerBalance - account.OpeningBalance),
            externalNet - cashBook.Accounts.Sum(account =>
                account.GeneralLedgerBalance - account.OpeningBalance),
            internalTransfers.Sum(row => row.NetMovement),
            cutoverAndNonCash.Sum(row => row.NetMovement),
            sections,
            internalTransfers,
            cutoverAndNonCash);
    }

    private static CashFlowSectionReadModel BuildSection(
        string activityType,
        IReadOnlyList<CashFlowRowReadModel> rows) =>
        new(
            activityType,
            rows.Sum(row => row.Inflow),
            rows.Sum(row => row.Outflow),
            rows.Sum(row => row.NetMovement),
            rows.OrderBy(row => row.PostingDate)
                .ThenBy(row => row.JournalNumber)
                .ThenBy(row => row.JournalEntryId)
                .ThenBy(row => row.JournalEntryLineId)
                .ToList());

    private static CashFlowRowReadModel MapRow(
        CashBookAccountReadModel account,
        CashBookRowReadModel row,
        string reason) =>
        new(
            row.JournalEntryId,
            row.JournalEntryLineId,
            row.PostingDate,
            row.BusinessDate,
            row.JournalNumber,
            row.JournalType,
            row.ReferenceType,
            row.ReferenceId,
            row.ReferenceNumber,
            row.Narration,
            account.AccountSlot,
            account.AccountId,
            account.AccountCode,
            account.AccountName,
            row.Inflow,
            row.Outflow,
            row.Inflow - row.Outflow,
            reason,
            row.CounterpartAccounts,
            row.CounterpartAccountIds,
            row.OutletId,
            row.WarehouseId,
            row.CustomerId,
            row.SupplierId,
            row.PaymentMethodId,
            row.CostCenterId,
            row.ReversalOfJournalEntryId,
            row.ReversedByJournalEntryId);

    private static (string ActivityType, string Reason) Classify(
        CashBookRowReadModel row,
        long? ownerCapitalAccountId,
        long? ownerDrawingsAccountId)
    {
        if (IsAny(row.JournalType, "OpeningBalance", "OpeningBalanceAdjustment", "YearEndClosing")
            || IsAny(row.ReferenceType, "OpeningBalance", "OpeningBalanceAdjustment", "FinancialYearClose"))
            return ("CutoverOrNonCash", "Opening/cutover or year-end non-cash activity is excluded from current-period Cash Flow.");

        if (IsAny(row.JournalType, "LiquidityTransfer")
            || IsAny(row.ReferenceType, "LiquidityTransfer"))
            return ("Internal", "Internal Cash/Petty Cash/Bank transfer is excluded from external Cash Flow totals.");

        if (IsAny(row.ReferenceType, "OwnerCapital", "OwnerDrawings")
            || IsAny(row.JournalType, "OwnerCapitalPosting", "OwnerDrawingsPosting")
            || (ownerCapitalAccountId.HasValue
                && row.CounterpartAccountIds.Contains(ownerCapitalAccountId.Value))
            || (ownerDrawingsAccountId.HasValue
                && row.CounterpartAccountIds.Contains(ownerDrawingsAccountId.Value)))
            return ("Financing", "Owner capital/drawings cash activity maps to Financing activities.");

        if (IsAny(row.ReferenceType, "FixedAssetPurchase", "FixedAssetDisposal", "InvestmentPurchase", "InvestmentSale")
            || IsAny(row.JournalType, "FixedAssetPurchasePosting", "FixedAssetDisposalPosting", "InvestmentPurchasePosting", "InvestmentSalePosting"))
            return ("Investing", "Investing source/reference type maps to Investing activities.");

        if (IsAny(row.ReferenceType, "Sale", "SaleRefund", "SupplierPayment", "SupplierAdvance", "Expense", "OtherIncome", "SettlementBatch", "CashSessionClose", "BankStatementAdjustment")
            || IsAny(row.JournalType, "SalePosting", "SalesReturnPosting", "ExpensePosting", "OtherIncomePosting", "SupplierPaymentPosting", "SupplierAdvancePosting", "CashDifferenceSurplusPosting", "CashDifferenceShortagePosting", "BankSettlement", "BankStatementAdjustment"))
            return ("Operating", "Ordinary trading, supplier, expense, other income, settlement, or bank adjustment activity maps to Operating activities.");

        return ("Unclassified", "No deterministic Cash Flow mapping exists for this journal/reference type.");
    }

    private static bool IsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(
            value,
            candidate,
            StringComparison.OrdinalIgnoreCase));

    private static void ValidateLocationType(string? locationType)
    {
        if (!string.IsNullOrWhiteSpace(locationType)
            && !string.Equals(locationType, "outlet", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(locationType, "warehouse", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "LocationType must be either outlet or warehouse.");
    }
}

public sealed record GetComparativeCashFlowQuery(CashFlowFilter Filter)
    : IQuery<ComparativeCashFlowReadModel>;

public sealed class GetComparativeCashFlowQueryHandler(ISender sender)
    : IRequestHandler<GetComparativeCashFlowQuery, ComparativeCashFlowReadModel>
{
    public async Task<ComparativeCashFlowReadModel> Handle(
        GetComparativeCashFlowQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.Filter;
        if (!filter.DateFrom.HasValue
            || !filter.DateTo.HasValue
            || !filter.ComparisonDateFrom.HasValue
            || !filter.ComparisonDateTo.HasValue)
            throw new InvalidOperationException(
                "Current and comparison date ranges are required for comparative Cash Flow reporting.");

        var current = await sender.Send(new GetCashFlowQuery(filter), cancellationToken);
        var comparison = await sender.Send(
            new GetCashFlowQuery(filter with
            {
                DateFrom = filter.ComparisonDateFrom,
                DateTo = filter.ComparisonDateTo
            }),
            cancellationToken);
        return new ComparativeCashFlowReadModel(current, comparison);
    }
}
