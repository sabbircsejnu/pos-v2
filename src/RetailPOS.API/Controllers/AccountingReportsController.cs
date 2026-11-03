using RetailPOS.Application.Common.Formatting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.ApReports.Queries;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using System.Text;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Application.VatReports.Queries;
using RetailPOS.Application.VatReports.ReadModels;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/reports")]
[Authorize]
public class AccountingReportsController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccess;
    private readonly ISender _sender;

    public AccountingReportsController(IUserOutletAccessService outletAccess, ISender sender)
    {
        _outletAccess = outletAccess;
        _sender = sender;
    }

    [HttpGet("vat-summary")]
    [Authorize(Policy = "accounting.reports.vat")]
    public async Task<ActionResult<ApiResponse<VatSummaryReportDto>>> GetVatSummary(
        [FromQuery] VatReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var report = await _sender.Send(
            new GetVatSummaryQuery(ToVatApplicationFilter(await ResolveVatLocationAsync(filter))),
            cancellationToken);
        return Ok(ApiResponse<VatSummaryReportDto>.SuccessResponse(ToApi(report)));
    }

    [HttpGet("vat-details")]
    [Authorize(Policy = "accounting.reports.vat")]
    public async Task<ActionResult<ApiResponse<List<VatTransactionDto>>>> GetVatDetails(
        [FromQuery] VatReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var report = await _sender.Send(
            new GetVatTransactionsQuery(ToVatApplicationFilter(await ResolveVatLocationAsync(filter))),
            cancellationToken);
        return Ok(ApiResponse<List<VatTransactionDto>>.SuccessResponse(
            report.Select(MapVatTransaction).ToList()));
    }

    [HttpGet("vat-reconciliation")]
    [Authorize(Policy = "accounting.reports.vat")]
    public async Task<ActionResult<ApiResponse<VatReconciliationReportDto>>> GetVatReconciliation(
        [FromQuery] VatReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var report = await _sender.Send(
            new GetVatReconciliationQuery(ToVatApplicationFilter(await ResolveVatLocationAsync(filter))),
            cancellationToken);
        return Ok(ApiResponse<VatReconciliationReportDto>.SuccessResponse(ToApi(report)));
    }

    [HttpGet("vat-exceptions")]
    [Authorize(Policy = "accounting.reports.vat")]
    public async Task<ActionResult<ApiResponse<VatExceptionReportDto>>> GetVatExceptions(
        [FromQuery] VatReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var report = await _sender.Send(
            new GetVatExceptionsQuery(ToVatApplicationFilter(await ResolveVatLocationAsync(filter))),
            cancellationToken);
        return Ok(ApiResponse<VatExceptionReportDto>.SuccessResponse(
            new VatExceptionReportDto
            {
                Items = report.Select(MapVatException).ToList()
            }));
    }

    [HttpGet("ap-subledger")]
    [Authorize(Policy = "accounting.reports.ap_subledger")]
    public async Task<ActionResult<ApiResponse<ApSubledgerReportDto>>> GetApSubledger(
        [FromQuery] ApReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var resolvedFilter = await ResolveApLocationAsync(filter);
        var report = await _sender.Send(
            new GetApSubledgerQuery(ApReportReadModelMapper.ToApplicationFilter(resolvedFilter)),
            cancellationToken);
        return Ok(ApiResponse<ApSubledgerReportDto>.SuccessResponse(
            ApReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("ap-aging")]
    [Authorize(Policy = "accounting.reports.ap_aging")]
    public async Task<ActionResult<ApiResponse<ApAgingReportDto>>> GetApAging(
        [FromQuery] ApReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var resolvedFilter = await ResolveApLocationAsync(filter);
        var report = await _sender.Send(
            new GetApAgingQuery(ApReportReadModelMapper.ToApplicationFilter(resolvedFilter)),
            cancellationToken);
        return Ok(ApiResponse<ApAgingReportDto>.SuccessResponse(
            ApReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("supplier-statement")]
    [Authorize(Policy = "accounting.reports.supplier_statement")]
    public async Task<ActionResult<ApiResponse<SupplierStatementReportDto>>> GetSupplierStatement(
        [FromQuery] ApReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var resolvedFilter = await ResolveApLocationAsync(filter);
        var report = await _sender.Send(
            new GetSupplierStatementQuery(ApReportReadModelMapper.ToApplicationFilter(resolvedFilter)),
            cancellationToken);
        return Ok(ApiResponse<SupplierStatementReportDto>.SuccessResponse(
            ApReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("ap-reconciliation")]
    [Authorize(Policy = "accounting.reports.ap_reconciliation")]
    public async Task<ActionResult<ApiResponse<ApReconciliationReportDto>>> GetApReconciliation(
        [FromQuery] ApReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var resolvedFilter = await ResolveApLocationAsync(filter);
        var report = await _sender.Send(
            new GetApReconciliationQuery(ApReportReadModelMapper.ToApplicationFilter(resolvedFilter)),
            cancellationToken);
        return Ok(ApiResponse<ApReconciliationReportDto>.SuccessResponse(
            ApReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("supplier-advance-reconciliation")]
    [Authorize(Policy = "accounting.reports.ap_reconciliation")]
    public async Task<ActionResult<ApiResponse<ApReconciliationReportDto>>> GetSupplierAdvanceReconciliation(
        [FromQuery] ApReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var resolvedFilter = await ResolveApLocationAsync(filter);
        var report = await _sender.Send(
            new GetSupplierAdvanceReconciliationQuery(
                ApReportReadModelMapper.ToApplicationFilter(resolvedFilter)),
            cancellationToken);
        return Ok(ApiResponse<ApReconciliationReportDto>.SuccessResponse(
            ApReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("general-ledger")]
    [Authorize(Policy = "accounting.reports.general_ledger")]
    public async Task<ActionResult<ApiResponse<GeneralLedgerReportDto>>> GetGeneralLedger(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] long? outletId,
        [FromQuery] long? financialYearId,
        [FromQuery] long? accountingPeriodId,
        CancellationToken cancellationToken)
    {
        var filter = new GeneralLedgerReportFilter(
            dateFrom,
            dateTo,
            outletId,
            financialYearId,
            accountingPeriodId);
        var report = await _sender.Send(new GetGeneralLedgerQuery(filter), cancellationToken);
        var result = AccountingReportReadModelMapper.ToApi(report);
        return Ok(ApiResponse<GeneralLedgerReportDto>.SuccessResponse(result));
    }

    [HttpGet("trial-balance")]
    [Authorize(Policy = "accounting.reports.trial_balance")]
    public async Task<ActionResult<ApiResponse<TrialBalanceReportDto>>> GetTrialBalance(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] long? outletId,
        [FromQuery] long? financialYearId,
        [FromQuery] long? accountingPeriodId,
        CancellationToken cancellationToken)
    {
        var filter = new GeneralLedgerReportFilter(
            dateFrom,
            dateTo,
            outletId,
            financialYearId,
            accountingPeriodId);
        var report = await _sender.Send(new GetTrialBalanceQuery(filter), cancellationToken);
        var result = AccountingReportReadModelMapper.ToApi(report);
        return Ok(ApiResponse<TrialBalanceReportDto>.SuccessResponse(result));
    }

    [HttpGet("profit-loss")]
    [Authorize(Policy = "accounting.reports.profit_loss")]
    public async Task<ActionResult<ApiResponse<ProfitLossReportDto>>> GetProfitLoss(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] long? outletId,
        [FromQuery] long? financialYearId,
        [FromQuery] long? accountingPeriodId,
        CancellationToken cancellationToken)
    {
        var filter = new GeneralLedgerReportFilter(
            dateFrom,
            dateTo,
            outletId,
            financialYearId,
            accountingPeriodId);
        var report = await _sender.Send(new GetProfitLossQuery(filter), cancellationToken);
        var result = AccountingReportReadModelMapper.ToApi(report);
        return Ok(ApiResponse<ProfitLossReportDto>.SuccessResponse(result));
    }

    [HttpGet("cogs")]
    [Authorize(Policy = "accounting.reports.cogs")]
    public async Task<ActionResult<ApiResponse<CogsReportDto>>> GetCogs(
        [FromQuery] ValuationReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var report = await _sender.Send(
            new GetCogsReportQuery(new CogsReportFilter(
                filter.DateFrom,
                filter.DateTo,
                filter.OutletId,
                filter.WarehouseId,
                filter.ProductId,
                filter.VariantId)),
            cancellationToken);
        return Ok(ApiResponse<CogsReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("inventory-valuation")]
    [Authorize(Policy = "accounting.reports.inventory_valuation")]
    public async Task<ActionResult<ApiResponse<InventoryValuationReportDto>>> GetInventoryValuation(
        [FromQuery] ValuationReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var report = await _sender.Send(
            new GetInventoryValuationQuery(
                AccountingReportReadModelMapper.ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<InventoryValuationReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("cash-book")]
    [Authorize(Policy = "accounting.reports.cash_book")]
    public async Task<ActionResult<ApiResponse<CashBookReportDto>>> GetCashBook(
        [FromQuery] CashBookReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var location = await _outletAccess.ResolveAndAuthorizeLocationAsync(filter.LocationId, filter.LocationType);
        filter.LocationId = location.LocationId;
        filter.LocationType = location.LocationType;
        var queryFilter = new CashBookFilter(
            filter.DateFrom,
            filter.DateTo,
            filter.LocationId,
            filter.LocationType,
            filter.JournalType,
            filter.ReferenceType,
            filter.Reference);
        var report = await _sender.Send(
            new GetCashBookQuery(queryFilter, filter.AccountSlots),
            cancellationToken);
        return Ok(ApiResponse<CashBookReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("cash-flow")]
    [Authorize(Policy = "accounting.reports.cash_flow")]
    public async Task<ActionResult<ApiResponse<CashFlowReportDto>>> GetCashFlow(
        [FromQuery] CashFlowReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        await ResolveCashFlowLocationAsync(filter);
        var report = await _sender.Send(
            new GetCashFlowQuery(AccountingReportReadModelMapper.ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<CashFlowReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("cash-flow/comparative")]
    [Authorize(Policy = "accounting.reports.cash_flow")]
    public async Task<ActionResult<ApiResponse<ComparativeCashFlowReportDto>>> GetComparativeCashFlow(
        [FromQuery] CashFlowReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        await ResolveCashFlowLocationAsync(filter);
        var report = await _sender.Send(
            new GetComparativeCashFlowQuery(
                AccountingReportReadModelMapper.ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<ComparativeCashFlowReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("cash-flow/export")]
    [Authorize(Policy = "accounting.reports.cash_flow")]
    public async Task<IActionResult> ExportCashFlow(
        [FromQuery] CashFlowReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        await ResolveCashFlowLocationAsync(filter);
        var report = filter.ComparisonDateFrom.HasValue || filter.ComparisonDateTo.HasValue
            ? AccountingReportReadModelMapper.ToApi(await _sender.Send(
                new GetComparativeCashFlowQuery(
                    AccountingReportReadModelMapper.ToApplicationFilter(filter)),
                cancellationToken))
            : new ComparativeCashFlowReportDto
            {
                Current = AccountingReportReadModelMapper.ToApi(await _sender.Send(
                    new GetCashFlowQuery(
                        AccountingReportReadModelMapper.ToApplicationFilter(filter)),
                    cancellationToken))
            };
        var settings = await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken);
        var bytes = Encoding.UTF8.GetBytes(ToCashFlowCsv(report, settings.Company.DateFormat));
        return File(bytes, "text/csv", $"CashFlow_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
    }

    [HttpGet("balance-sheet")]
    [Authorize(Policy = "accounting.reports.balance_sheet")]
    public async Task<ActionResult<ApiResponse<BalanceSheetReportDto>>> GetBalanceSheet(
        [FromQuery] FinancialStatementFilterDto filter,
        CancellationToken cancellationToken)
    {
        await ResolveAuthorizedLocationAsync(filter);
        var applicationFilter = new BalanceSheetReportFilter(
            filter.AsOfDate,
            filter.LocationId,
            filter.LocationType);
        var report = await _sender.Send(
            new GetBalanceSheetQuery(applicationFilter),
            cancellationToken);
        return Ok(ApiResponse<BalanceSheetReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("balance-sheet/comparative")]
    [Authorize(Policy = "accounting.reports.balance_sheet")]
    public async Task<ActionResult<ApiResponse<ComparativeBalanceSheetReportDto>>> GetComparativeBalanceSheet(
        [FromQuery] FinancialStatementFilterDto filter,
        CancellationToken cancellationToken)
    {
        await ResolveAuthorizedLocationAsync(filter);
        var applicationFilter = new BalanceSheetReportFilter(
            filter.AsOfDate,
            filter.LocationId,
            filter.LocationType);
        var report = await _sender.Send(
            new GetComparativeBalanceSheetQuery(applicationFilter, filter.ComparisonAsOfDate),
            cancellationToken);
        return Ok(ApiResponse<ComparativeBalanceSheetReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("enhanced-general-ledger")]
    [Authorize(Policy = "accounting.reports.general_ledger")]
    public async Task<ActionResult<ApiResponse<EnhancedGeneralLedgerReportDto>>> GetEnhancedGeneralLedger(
        [FromQuery] EnhancedGeneralLedgerFilterDto filter,
        CancellationToken cancellationToken)
    {
        var location = await _outletAccess.ResolveAndAuthorizeLocationAsync(filter.LocationId, filter.LocationType);
        filter.LocationId = location.LocationId;
        filter.LocationType = location.LocationType;
        var applicationFilter = new EnhancedGeneralLedgerFilter(
            filter.AccountId,
            filter.DateFrom,
            filter.DateTo,
            filter.LocationId,
            filter.LocationType,
            filter.JournalType,
            filter.ReferenceType,
            filter.Reference);
        var report = await _sender.Send(
            new GetEnhancedGeneralLedgerQuery(applicationFilter),
            cancellationToken);
        return Ok(ApiResponse<EnhancedGeneralLedgerReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("profit-loss/comparative")]
    [Authorize(Policy = "accounting.reports.profit_loss")]
    public async Task<ActionResult<ApiResponse<ComparativeProfitLossReportDto>>> GetComparativeProfitLoss(
        [FromQuery] ComparativeProfitLossFilterDto filter,
        CancellationToken cancellationToken)
    {
        var location = await _outletAccess.ResolveAndAuthorizeLocationAsync(filter.LocationId, filter.LocationType);
        filter.LocationId = location.LocationId;
        filter.LocationType = location.LocationType;
        var applicationFilter = new ComparativeProfitLossReportFilter(
            filter.DateFrom,
            filter.DateTo,
            filter.ComparisonDateFrom,
            filter.ComparisonDateTo,
            filter.LocationId,
            filter.LocationType);
        var report = await _sender.Send(
            new GetComparativeProfitLossQuery(applicationFilter),
            cancellationToken);
        return Ok(ApiResponse<ComparativeProfitLossReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("business-profitability")]
    [Authorize(Policy = "accounting.reports.profitability")]
    public async Task<ActionResult<ApiResponse<BusinessProfitabilityReportDto>>> GetBusinessProfitability(
        [FromQuery] ProfitabilityReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        await ResolveProfitabilityLocationAsync(filter);
        var report = await _sender.Send(
            new GetBusinessProfitabilityQuery(
                AccountingReportReadModelMapper.ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<BusinessProfitabilityReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("business-profitability/comparative")]
    [Authorize(Policy = "accounting.reports.profitability")]
    public async Task<ActionResult<ApiResponse<ComparativeBusinessProfitabilityReportDto>>> GetComparativeBusinessProfitability(
        [FromQuery] ProfitabilityReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        await ResolveProfitabilityLocationAsync(filter);
        var report = await _sender.Send(
            new GetComparativeBusinessProfitabilityQuery(
                AccountingReportReadModelMapper.ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<ComparativeBusinessProfitabilityReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("outlet-profitability")]
    [Authorize(Policy = "accounting.reports.profitability")]
    public async Task<ActionResult<ApiResponse<OutletProfitabilityReportDto>>> GetOutletProfitability(
        [FromQuery] ProfitabilityReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var report = await _sender.Send(
            new GetOutletProfitabilityQuery(
                AccountingReportReadModelMapper.ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<OutletProfitabilityReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("outlet-profitability/comparative")]
    [Authorize(Policy = "accounting.reports.profitability")]
    public async Task<ActionResult<ApiResponse<ComparativeOutletProfitabilityReportDto>>> GetComparativeOutletProfitability(
        [FromQuery] ProfitabilityReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var report = await _sender.Send(
            new GetComparativeOutletProfitabilityQuery(
                AccountingReportReadModelMapper.ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<ComparativeOutletProfitabilityReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("product-profitability")]
    [Authorize(Policy = "accounting.reports.profitability")]
    public async Task<ActionResult<ApiResponse<ProductContributionReportDto>>> GetProductContribution(
        [FromQuery] ProfitabilityReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        await ResolveProfitabilityLocationAsync(filter);
        var report = await _sender.Send(
            new GetProductContributionQuery(
                AccountingReportReadModelMapper.ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<ProductContributionReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("product-profitability/comparative")]
    [Authorize(Policy = "accounting.reports.profitability")]
    public async Task<ActionResult<ApiResponse<ComparativeProductContributionReportDto>>> GetComparativeProductContribution(
        [FromQuery] ProfitabilityReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        await ResolveProfitabilityLocationAsync(filter);
        var report = await _sender.Send(
            new GetComparativeProductContributionQuery(
                AccountingReportReadModelMapper.ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<ComparativeProductContributionReportDto>.SuccessResponse(
            AccountingReportReadModelMapper.ToApi(report)));
    }

    [HttpGet("profitability/export")]
    [Authorize(Policy = "accounting.reports.profitability")]
    public async Task<IActionResult> ExportProfitability(
        [FromQuery] ProfitabilityReportFilterDto filter,
        [FromQuery] string reportType = "business",
        CancellationToken cancellationToken = default)
    {
        await ResolveProfitabilityLocationAsync(filter);
        var applicationFilter = AccountingReportReadModelMapper.ToApplicationFilter(filter);
        var dateFormat = (await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken)).Company.DateFormat;
        var csv = reportType.Trim().ToLowerInvariant() switch
        {
            "outlet" => ToOutletProfitabilityCsv(filter.ComparisonDateFrom.HasValue || filter.ComparisonDateTo.HasValue
                ? AccountingReportReadModelMapper.ToApi(await _sender.Send(
                    new GetComparativeOutletProfitabilityQuery(applicationFilter),
                    cancellationToken))
                : new ComparativeOutletProfitabilityReportDto
                {
                    Current = AccountingReportReadModelMapper.ToApi(await _sender.Send(
                        new GetOutletProfitabilityQuery(applicationFilter),
                        cancellationToken))
                }),
            "product" or "category" => ToProductContributionCsv(filter.ComparisonDateFrom.HasValue || filter.ComparisonDateTo.HasValue
                ? AccountingReportReadModelMapper.ToApi(await _sender.Send(
                    new GetComparativeProductContributionQuery(applicationFilter),
                    cancellationToken))
                : new ComparativeProductContributionReportDto
                {
                    Current = AccountingReportReadModelMapper.ToApi(await _sender.Send(
                        new GetProductContributionQuery(applicationFilter),
                        cancellationToken))
                }),
            _ => ToBusinessProfitabilityCsv(filter.ComparisonDateFrom.HasValue || filter.ComparisonDateTo.HasValue
                ? AccountingReportReadModelMapper.ToApi(await _sender.Send(
                    new GetComparativeBusinessProfitabilityQuery(applicationFilter),
                    cancellationToken))
                : new ComparativeBusinessProfitabilityReportDto
                {
                    Current = AccountingReportReadModelMapper.ToApi(await _sender.Send(
                        new GetBusinessProfitabilityQuery(applicationFilter),
                        cancellationToken))
                }, dateFormat)
        };
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"Profitability_{reportType}_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
    }

    private async Task ResolveAuthorizedLocationAsync(FinancialStatementFilterDto filter)
    {
        var location = await _outletAccess.ResolveAndAuthorizeLocationAsync(filter.LocationId, filter.LocationType);
        filter.LocationId = location.LocationId;
        filter.LocationType = location.LocationType;
    }

    private async Task ResolveCashFlowLocationAsync(CashFlowReportFilterDto filter)
    {
        var location = await _outletAccess.ResolveAndAuthorizeLocationAsync(filter.LocationId, filter.LocationType);
        filter.LocationId = location.LocationId;
        filter.LocationType = location.LocationType;
    }

    private async Task ResolveProfitabilityLocationAsync(ProfitabilityReportFilterDto filter)
    {
        if (filter.LocationId.HasValue || !string.IsNullOrWhiteSpace(filter.LocationType))
        {
            var location = await _outletAccess.ResolveAndAuthorizeLocationAsync(filter.LocationId, filter.LocationType);
            filter.LocationId = location.LocationId;
            filter.LocationType = location.LocationType;
        }
    }

    private static string ToBusinessProfitabilityCsv(ComparativeBusinessProfitabilityReportDto report, string dateFormat)
    {
        var builder = new StringBuilder();
        AppendBusinessProfitabilitySummary(builder, "Current", report.Current, dateFormat);
        if (report.Comparison.DateFrom != default) AppendBusinessProfitabilitySummary(builder, "Comparison", report.Comparison, dateFormat);
        return builder.ToString();
    }

    private static void AppendBusinessProfitabilitySummary(StringBuilder builder, string period, BusinessProfitabilityReportDto report, string dateFormat)
    {
        builder.AppendLine($"{period} Business Profitability");
        builder.AppendLine("Date From,Date To,Revenue,COGS,Expenses,Net Profit,Authoritative P&L Net Profit,Reconciliation Difference");
        builder.AppendLine(string.Join(',', Csv(UiDateFormatting.Format(report.DateFrom, dateFormat)), Csv(UiDateFormatting.Format(report.DateTo, dateFormat)), report.Revenue, report.CostOfGoodsSold, report.Expenses, report.NetProfit, report.AuthoritativePnlNetProfit, report.ReconciliationDifference));
        builder.AppendLine();
    }

    private static string ToOutletProfitabilityCsv(ComparativeOutletProfitabilityReportDto report)
    {
        var builder = new StringBuilder();
        AppendOutletProfitabilityRows(builder, "Current", report.Current);
        if (report.Comparison.DateFrom != default) AppendOutletProfitabilityRows(builder, "Comparison", report.Comparison);
        return builder.ToString();
    }

    private static void AppendOutletProfitabilityRows(StringBuilder builder, string period, OutletProfitabilityReportDto report)
    {
        builder.AppendLine($"{period} Outlet Profitability");
        builder.AppendLine("OutletId,OutletName,Revenue,COGS,Expenses,Net Profit,Unallocated Reason");
        foreach (var row in report.Outlets.Concat(new[] { report.Unallocated }))
            builder.AppendLine(string.Join(',', row.OutletId?.ToString() ?? string.Empty, Csv(row.OutletName), row.Revenue, row.CostOfGoodsSold, row.Expenses, row.NetProfit, Csv(row.UnallocatedReason)));
        builder.AppendLine($"Business Totals,,{report.BusinessRevenue},{report.BusinessCostOfGoodsSold},{report.BusinessExpenses},{report.BusinessNetProfit},");
        builder.AppendLine($"Attributed Plus Unallocated,,{report.AttributedRevenue},{report.AttributedCostOfGoodsSold},{report.AttributedExpenses},{report.AttributedNetProfit},");
        builder.AppendLine($"Reconciliation Difference,,,,{report.ReconciliationDifference},");
        builder.AppendLine();
    }

    private static string ToProductContributionCsv(ComparativeProductContributionReportDto report)
    {
        var builder = new StringBuilder();
        AppendProductContributionRows(builder, "Current", report.Current);
        if (report.Comparison.DateFrom != default) AppendProductContributionRows(builder, "Comparison", report.Comparison);
        return builder.ToString();
    }

    private static void AppendProductContributionRows(StringBuilder builder, string period, ProductContributionReportDto report)
    {
        builder.AppendLine($"{period} Product Contribution");
        builder.AppendLine(Csv(report.ContributionBasis));
        builder.AppendLine("ProductId,ProductName,ProductCode,CategoryId,CategoryName,Revenue,COGS,Contribution,MarginPercent");
        foreach (var row in report.Products)
            builder.AppendLine(string.Join(',', row.ProductId, Csv(row.ProductName), Csv(row.ProductCode), row.CategoryId, Csv(row.CategoryName), row.Revenue, row.CostOfGoodsSold, row.Contribution, row.MarginPercent));
        builder.AppendLine();
        builder.AppendLine("CategoryId,CategoryName,Revenue,COGS,Contribution,MarginPercent");
        foreach (var row in report.Categories)
            builder.AppendLine(string.Join(',', row.CategoryId, Csv(row.CategoryName), row.Revenue, row.CostOfGoodsSold, row.Contribution, row.MarginPercent));
        builder.AppendLine($"Attributed Revenue,{report.AttributedRevenue}");
        builder.AppendLine($"Attributed COGS,{report.AttributedCostOfGoodsSold}");
        builder.AppendLine($"Unallocated Revenue,{report.UnallocatedRevenue}");
        builder.AppendLine($"Unallocated COGS,{report.UnallocatedCostOfGoodsSold}");
        builder.AppendLine($"Revenue Reconciliation Difference,{report.RevenueReconciliationDifference}");
        builder.AppendLine($"COGS Reconciliation Difference,{report.CogsReconciliationDifference}");
        builder.AppendLine($"Historical Limitation,{Csv(report.HistoricalLimitation)}");
        builder.AppendLine();
    }

    private static string ToCashFlowCsv(ComparativeCashFlowReportDto report, string dateFormat)
    {
        var builder = new StringBuilder();
        AppendCashFlowSummary(builder, "Current", report.Current, dateFormat);
        if (report.Comparison.DateFrom != default)
            AppendCashFlowSummary(builder, "Comparison", report.Comparison, dateFormat);
        builder.AppendLine();
        builder.AppendLine("Period,Section,Date,JournalNumber,ReferenceType,ReferenceId,AccountCode,AccountName,Inflow,Outflow,NetMovement,ClassificationReason,CounterpartAccounts");
        AppendCashFlowRows(builder, "Current", report.Current, dateFormat);
        if (report.Comparison.DateFrom != default)
            AppendCashFlowRows(builder, "Comparison", report.Comparison, dateFormat);
        return builder.ToString();
    }

    private static void AppendCashFlowSummary(StringBuilder builder, string period, CashFlowReportDto report, string dateFormat)
    {
        builder.AppendLine($"{period} Cash Flow Statement");
        builder.AppendLine($"Date From,{Csv(UiDateFormatting.Format(report.DateFrom, dateFormat))}");
        builder.AppendLine($"Date To,{Csv(UiDateFormatting.Format(report.DateTo, dateFormat))}");
        builder.AppendLine($"Opening Liquidity,{report.OpeningLiquidity}");
        builder.AppendLine($"Operating Net Movement,{report.OperatingNetMovement}");
        builder.AppendLine($"Investing Net Movement,{report.InvestingNetMovement}");
        builder.AppendLine($"Financing Net Movement,{report.FinancingNetMovement}");
        builder.AppendLine($"Unclassified Net Movement,{report.UnclassifiedNetMovement}");
        builder.AppendLine($"Total External Net Movement,{report.TotalExternalNetMovement}");
        builder.AppendLine($"Closing Liquidity,{report.ClosingLiquidity}");
        builder.AppendLine($"GL-Derived Movement,{report.GlDerivedMovement}");
        builder.AppendLine($"Reconciliation Difference,{report.ReconciliationDifference}");
        builder.AppendLine();
    }

    private static void AppendCashFlowRows(StringBuilder builder, string period, CashFlowReportDto report, string dateFormat)
    {
        foreach (var section in report.Sections)
            foreach (var row in section.Rows)
                AppendCashFlowRow(builder, period, section.ActivityType, row, dateFormat);
        foreach (var row in report.InternalTransferRows)
            AppendCashFlowRow(builder, period, "InternalTransferExcluded", row, dateFormat);
        foreach (var row in report.CutoverAndNonCashRows)
            AppendCashFlowRow(builder, period, "CutoverOrNonCashExcluded", row, dateFormat);
    }

    private static void AppendCashFlowRow(StringBuilder builder, string period, string section, CashFlowRowDto row, string dateFormat)
        => builder.AppendLine(string.Join(',', new[]
        {
            Csv(period), Csv(section), Csv(UiDateFormatting.Format(row.PostingDate, dateFormat)), Csv(row.JournalNumber), Csv(row.ReferenceType), Csv(row.ReferenceId),
            Csv(row.AccountCode), Csv(row.AccountName), row.Inflow.ToString(), row.Outflow.ToString(), row.NetMovement.ToString(), Csv(row.ClassificationReason), Csv(row.CounterpartAccounts)
        }));

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private async Task<ApReportFilterDto> ResolveApLocationAsync(ApReportFilterDto filter)
    {
        if (filter.LocationId.HasValue || !string.IsNullOrWhiteSpace(filter.LocationType))
        {
            var location = await _outletAccess.ResolveAndAuthorizeLocationAsync(filter.LocationId, filter.LocationType);
            filter.LocationId = location.LocationId;
            filter.LocationType = location.LocationType;
            if (string.Equals(location.LocationType, "warehouse", StringComparison.OrdinalIgnoreCase)) filter.WarehouseId = location.LocationId;
            if (string.Equals(location.LocationType, "outlet", StringComparison.OrdinalIgnoreCase)) filter.OutletId = location.LocationId;
        }
        return filter;
    }

    private static VatReportFilter ToVatApplicationFilter(VatReportFilterDto filter) =>
        new(
            filter.DateFrom,
            filter.DateTo,
            filter.OutletId,
            filter.WarehouseId,
            filter.VatDirection,
            filter.SourceType,
            filter.SourceReference,
            filter.TaxCode,
            filter.VatRate,
            filter.LocationId,
            filter.LocationType);

    private static VatSummaryReportDto ToApi(VatSummaryReadModel report) =>
        new()
        {
            DateFrom = report.DateFrom,
            DateTo = report.DateTo,
            NetTaxableSales = report.NetTaxableSales,
            OutputVatFromSales = report.OutputVatFromSales,
            SalesReturnVatAdjustments = report.SalesReturnVatAdjustments,
            NetTaxableOtherIncome = report.NetTaxableOtherIncome,
            OtherIncomeOutputVat = report.OtherIncomeOutputVat,
            TotalOutputVat = report.TotalOutputVat,
            NetTaxablePurchases = report.NetTaxablePurchases,
            PurchaseInputVat = report.PurchaseInputVat,
            NetTaxableExpenses = report.NetTaxableExpenses,
            ExpenseInputVat = report.ExpenseInputVat,
            TotalInputVat = report.TotalInputVat,
            NetVatPosition = report.NetVatPosition,
            Position = report.Position,
            ExceptionCount = report.ExceptionCount,
            Breakdown = report.Breakdown.Select(item => new VatRateBreakdownDto
            {
                TaxCode = item.TaxCode,
                VatRate = item.VatRate,
                TaxableAmount = item.TaxableAmount,
                OutputVat = item.OutputVat,
                InputVat = item.InputVat
            }).ToList()
        };

    private static VatTransactionDto MapVatTransaction(VatTransactionReadModel transaction) =>
        new()
        {
            PostingDate = transaction.PostingDate,
            SourceType = transaction.SourceType,
            SourceReference = transaction.SourceReference,
            JournalEntryId = transaction.JournalEntryId,
            JournalNumber = transaction.JournalNumber,
            OutletId = transaction.OutletId,
            WarehouseId = transaction.WarehouseId,
            TaxCode = transaction.TaxCode,
            VatRate = transaction.VatRate,
            TaxableAmount = transaction.TaxableAmount,
            VatAmount = transaction.VatAmount,
            VatDirection = transaction.VatDirection,
            OriginalJournalEntryId = transaction.OriginalJournalEntryId,
            ExceptionReason = transaction.ExceptionReason
        };

    private static VatReconciliationReportDto ToApi(VatReconciliationReadModel report) =>
        new()
        {
            ReportOutputVat = report.ReportOutputVat,
            GlOutputVat = report.GlOutputVat,
            OutputDifference = report.OutputDifference,
            OutputStatus = report.OutputStatus,
            ReportInputVat = report.ReportInputVat,
            GlInputVat = report.GlInputVat,
            InputDifference = report.InputDifference,
            InputStatus = report.InputStatus,
            ReportNetVatPosition = report.ReportNetVatPosition,
            GlNetVatPosition = report.GlNetVatPosition,
            OverallDifference = report.OverallDifference
        };

    private static VatExceptionDto MapVatException(VatExceptionReadModel exception) =>
        new()
        {
            PostingDate = exception.PostingDate,
            SourceType = exception.SourceType,
            SourceReference = exception.SourceReference,
            JournalEntryId = exception.JournalEntryId,
            JournalNumber = exception.JournalNumber,
            Reason = exception.Reason,
            KnownVatAmount = exception.KnownVatAmount
        };

    private async Task<VatReportFilterDto> ResolveVatLocationAsync(VatReportFilterDto filter)
    {
        if (filter.LocationId.HasValue || !string.IsNullOrWhiteSpace(filter.LocationType))
        {
            var location = await _outletAccess.ResolveAndAuthorizeLocationAsync(filter.LocationId, filter.LocationType);
            filter.LocationId = location.LocationId;
            filter.LocationType = location.LocationType;
            if (string.Equals(location.LocationType, "warehouse", StringComparison.OrdinalIgnoreCase)) filter.WarehouseId = location.LocationId;
            if (string.Equals(location.LocationType, "outlet", StringComparison.OrdinalIgnoreCase)) filter.OutletId = location.LocationId;
        }
        return filter;
    }
}
