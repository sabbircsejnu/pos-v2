namespace RetailPOS.Application.AccountingCloseRuns.Abstractions;

public interface IAccountingPeriodCloseFinancialGateEvaluator
{
    Task<IReadOnlyList<AccountingPeriodCloseFinancialGateResult>> EvaluateAsync(
        DateTime periodStart,
        DateTime periodEnd,
        long financialYearId,
        long accountingPeriodId,
        CancellationToken cancellationToken);
}

public sealed record AccountingPeriodCloseFinancialGateResult(
    string GateCode,
    string Status,
    decimal? ActualAmount,
    string Details,
    bool IsHistoricalCutoverLimitation = false);
