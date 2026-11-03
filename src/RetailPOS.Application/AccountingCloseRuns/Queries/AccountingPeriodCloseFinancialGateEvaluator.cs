using MediatR;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;
using RetailPOS.Application.AccountingConfiguration.Queries;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Application.ApReports.Queries;
using RetailPOS.Application.ApReports.ReadModels;
using RetailPOS.Application.VatReports.Queries;
using RetailPOS.Application.VatReports.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.AccountingCloseRuns.Queries;

public sealed class AccountingPeriodCloseFinancialGateEvaluator(ISender sender)
    : IAccountingPeriodCloseFinancialGateEvaluator
{
    public async Task<IReadOnlyList<AccountingPeriodCloseFinancialGateResult>> EvaluateAsync(
        DateTime periodStart,
        DateTime periodEnd,
        long financialYearId,
        long accountingPeriodId,
        CancellationToken cancellationToken)
    {
        var gates = new List<AccountingPeriodCloseFinancialGateResult>();
        var configurationErrors = await sender.Send(
            new ValidateRequiredAccountingMappingsQuery(),
            cancellationToken);
        gates.Add(new AccountingPeriodCloseFinancialGateResult(
            "configuration",
            configurationErrors.Count == 0
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            configurationErrors.Count,
            configurationErrors.Count == 0
                ? "Required accounting mappings are valid."
                : string.Join(" ", configurationErrors)));

        if (configurationErrors.Count > 0)
            return gates;

        cancellationToken.ThrowIfCancellationRequested();
        var trialBalance = await sender.Send(
            new GetTrialBalanceQuery(new GeneralLedgerReportFilter(
                periodStart,
                periodEnd,
                null,
                financialYearId,
                accountingPeriodId)),
            cancellationToken);
        gates.Add(new AccountingPeriodCloseFinancialGateResult(
            "trial_balance",
            trialBalance.TotalDebits == trialBalance.TotalCredits
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            trialBalance.TotalDebits - trialBalance.TotalCredits,
            "Trial Balance debits less credits."));

        cancellationToken.ThrowIfCancellationRequested();
        var balanceSheet = await sender.Send(
            new GetBalanceSheetQuery(new BalanceSheetReportFilter(periodEnd, null, null)),
            cancellationToken);
        gates.Add(new AccountingPeriodCloseFinancialGateResult(
            "balance_sheet",
            balanceSheet.ReconciliationDifference == 0m
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            balanceSheet.ReconciliationDifference,
            "Balance Sheet assets less liabilities and equity."));

        cancellationToken.ThrowIfCancellationRequested();
        var asOfFilter = new ApPayablesReportFilter(
            periodEnd,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var payable = await sender.Send(
            new GetApReconciliationQuery(asOfFilter),
            cancellationToken);
        var advance = await sender.Send(
            new GetSupplierAdvanceReconciliationQuery(asOfFilter),
            cancellationToken);
        gates.Add(new AccountingPeriodCloseFinancialGateResult(
            "supplier_payable",
            payable.ReconciliationDifference == 0m
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            payable.ReconciliationDifference,
            "Supplier Payable subledger less GL."));
        gates.Add(new AccountingPeriodCloseFinancialGateResult(
            "supplier_advance",
            advance.ReconciliationDifference == 0m
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            advance.ReconciliationDifference,
            "Supplier Advance subledger less GL."));

        cancellationToken.ThrowIfCancellationRequested();
        var vatFilter = new VatReportFilter(periodStart, periodEnd);
        var vatReconciliation = await sender.Send(
            new GetVatReconciliationQuery(vatFilter),
            cancellationToken);
        gates.Add(new AccountingPeriodCloseFinancialGateResult(
            "vat",
            vatReconciliation.OverallDifference == 0m
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            vatReconciliation.OverallDifference,
            "VAT report net position less GL net position."));

        cancellationToken.ThrowIfCancellationRequested();
        var inventory = await sender.Send(
            new GetInventoryValuationQuery(new InventoryValuationFilter(
                periodEnd,
                null,
                null,
                null,
                null,
                null,
                null)),
            cancellationToken);
        gates.Add(new AccountingPeriodCloseFinancialGateResult(
            "inventory",
            inventory.ReconciliationDifference == 0m
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            inventory.ReconciliationDifference,
            "Inventory valuation less Inventory GL."));
        if (inventory.UnvaluedMovements.Count > 0)
        {
            gates.Add(new AccountingPeriodCloseFinancialGateResult(
                "inventory_unvalued",
                AccountingCloseGateStatuses.Warning,
                inventory.UnvaluedMovements.Count,
                "Unvalued inventory movements are visible as historical/cutover evidence.",
                true));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var vatExceptions = await sender.Send(
            new GetVatExceptionsQuery(vatFilter),
            cancellationToken);
        if (vatExceptions.Count > 0)
        {
            gates.Add(new AccountingPeriodCloseFinancialGateResult(
                "vat_legacy",
                AccountingCloseGateStatuses.Warning,
                vatExceptions.Count,
                "Incomplete VAT snapshot evidence is visible.",
                true));
        }

        return gates;
    }
}
