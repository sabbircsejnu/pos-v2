using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollAccountingPostingStore(RetailPOSDbContext db) : IPayrollAccountingPostingStore
{
    public async Task<PayrollAccountingSource?> FindSourceAsync(
        long businessId, long runId, CancellationToken cancellationToken)
    {
        var run = await (from candidate in db.PayrollRuns.AsNoTracking()
                         join period in db.PayrollPeriods.AsNoTracking()
                             on new { candidate.BusinessId, PeriodId = candidate.PayrollPeriodId }
                             equals new { period.BusinessId, PeriodId = period.Id }
                         where candidate.BusinessId == businessId && candidate.Id == runId
                         select new PayrollAccountingSourceRow(
                             candidate.BusinessId, candidate.PayrollPeriodId, candidate.Id,
                             candidate.RunNo, candidate.RunType, candidate.RunReference,
                             candidate.CorrectionReference, candidate.OriginalPayrollRunId,
                             candidate.ScopeType, candidate.ScopeId, candidate.Status,
                             candidate.CalculationVersion, candidate.CalculationInputHash,
                             candidate.ApprovalEvidenceHash, candidate.CurrencyCode,
                             candidate.DecimalPrecision, period.Status, period.PeriodStart, period.PeriodEnd,
                             period.PayDate))
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null)
            return null;

        var results = await db.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == runId
                && row.CalculationVersion == run.Revision)
            .OrderBy(row => row.EmployeeId)
            .Select(row => new PayrollAccountingResultRow(
                row.Id, row.EmployeeId, row.LocationIdSnapshot, row.CalculationSnapshotJson,
                row.CalculationSnapshotHash, row.GrossEarnings, row.TotalDeductions, row.NetPay,
                row.Status))
            .ToArrayAsync(cancellationToken);
        var resultIds = results.Select(row => row.Id).ToList();
        var lines = resultIds.Count == 0
            ? []
            : await db.PayrollEmployeeResultLines.AsNoTracking()
                .Where(row => row.BusinessId == businessId && resultIds.Contains(row.PayrollEmployeeResultId))
                .OrderBy(row => row.PayrollEmployeeResultId)
                .ThenBy(row => row.CalculationOrder)
                .ThenBy(row => row.Id)
                .Select(row => new PayrollAccountingLineRow(
                    row.PayrollEmployeeResultId, row.Id, row.ComponentCodeSnapshot, row.LineType, row.Amount))
                .ToArrayAsync(cancellationToken);
        var linesByResult = lines.GroupBy(row => row.ResultId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PayrollAccountingComponentLine>)
                group.Select(row => new PayrollAccountingComponentLine(
                    row.Id, row.ComponentCode, row.LineType, row.Amount)).ToArray());

        var selections = await db.PayrollRunEmployeeSelections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == runId && row.IsIncluded)
            .CountAsync(cancellationToken);
        if (results.Length != selections)
            throw new InvalidDataException("Approved Payroll run results do not match its frozen employee selection.");

        var employees = results.Select(row =>
        {
            if (row.Status != PayrollEmployeeResultStatuses.Calculated
                || row.SnapshotJson is null || row.SnapshotHash is null
                || !StringComparer.Ordinal.Equals(Hash(row.SnapshotJson), row.SnapshotHash)
                || row.Gross is null || row.Deductions is null || row.Net is null)
                throw new InvalidDataException("Approved Payroll result evidence is incomplete or has an invalid hash.");

            var resultLines = linesByResult.GetValueOrDefault(row.Id, []);
            if (resultLines.Any(line => string.IsNullOrWhiteSpace(line.ComponentCode)
                    || line.Amount < 0m
                    || line.LineType is not (PayrollAccountingComponentTypes.Earning
                        or PayrollAccountingComponentTypes.Deduction))
                || resultLines.Where(line => line.LineType == PayrollAccountingComponentTypes.Earning)
                    .Sum(line => line.Amount) != row.Gross
                || resultLines.Where(line => line.LineType == PayrollAccountingComponentTypes.Deduction)
                    .Sum(line => line.Amount) != row.Deductions
                || row.Gross != row.Net + row.Deductions)
                throw new InvalidDataException("Persisted Payroll result totals do not reconcile to their component lines.");

            return new PayrollAccountingEmployeeSource(
                row.Id, row.EmployeeId, row.LocationId, row.SnapshotJson, row.SnapshotHash,
                row.Gross.Value, row.Deductions.Value, row.Net.Value, resultLines);
        }).ToArray();

        return new PayrollAccountingSource(
            run.BusinessId, run.PeriodId, run.Id, run.RunNo, run.RunType, run.RunReference,
            run.CorrectionReference, run.OriginalRunId, run.ScopeType, run.ScopeId, run.Status,
            run.Revision, run.CalculationHash, run.ApprovalHash, run.Currency,
            run.Precision, run.PeriodStatus, run.PeriodStart, run.PeriodEnd, run.PayDate, employees);
    }

    public Task<PayrollAccountingPosting?> FindPostingAsync(
        long businessId, long runId, int revision, CancellationToken cancellationToken) =>
        db.PayrollAccountingPostings.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.PayrollRunId == runId && row.RunRevision == revision
            && row.PostingType == PayrollAccountingPostingTypes.PayrollExpense, cancellationToken);

    public Task<PayrollAccountingPosting?> FindCorrectionPostingAsync(
        long businessId, long runId, int revision, CancellationToken cancellationToken) =>
        db.PayrollAccountingPostings.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.PayrollRunId == runId && row.RunRevision == revision
            && row.PostingType == PayrollAccountingPostingTypes.PayrollCorrection, cancellationToken);

    public Task<PayrollAccountingPosting?> FindCorrectionByOriginalAsync(
        long businessId, long originalPostingId, CancellationToken cancellationToken) =>
        db.PayrollAccountingPostings.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId
            && row.PostingType == PayrollAccountingPostingTypes.PayrollCorrection
            && row.OriginalPostingId == originalPostingId, cancellationToken);

    public async Task<PayrollAccountingSettlementSource?> FindSettlementSourceAsync(
        long businessId,
        long paymentId,
        long allocationId,
        long evidenceId,
        CancellationToken cancellationToken)
    {
        var row = await (from payment in db.PayrollPayments.AsNoTracking()
                         join run in db.PayrollRuns.AsNoTracking()
                             on new { payment.BusinessId, Id = payment.PayrollRunId!.Value }
                             equals new { run.BusinessId, run.Id }
                         join period in db.PayrollPeriods.AsNoTracking()
                             on new { payment.BusinessId, Id = payment.PayrollPeriodId!.Value }
                             equals new { period.BusinessId, period.Id }
                         join result in db.PayrollEmployeeResults.AsNoTracking()
                             on new { payment.BusinessId, Id = payment.PayrollEmployeeResultId!.Value }
                             equals new { result.BusinessId, result.Id }
                         join allocation in db.PayrollPaymentAllocations.AsNoTracking()
                             on new { payment.BusinessId, PayrollPaymentId = payment.Id }
                             equals new { allocation.BusinessId, allocation.PayrollPaymentId }
                         join evidence in db.PayrollPaymentEvidence.AsNoTracking()
                             on new { payment.BusinessId, PayrollPaymentId = payment.Id }
                             equals new { evidence.BusinessId, evidence.PayrollPaymentId }
                         where payment.BusinessId == businessId && payment.Id == paymentId
                             && payment.PayrollRunId != null
                             && payment.PayrollPeriodId != null
                             && payment.PayrollEmployeeResultId != null
                             && allocation.Id == allocationId && evidence.Id == evidenceId
                             && result.CalculationVersion == run.CalculationVersion
                         select new PayrollAccountingSettlementSourceRow(
                             payment.BusinessId, payment.PayrollPeriodId.Value, run.Id, run.RunNo,
                             run.RunType, run.RunReference, run.ScopeType, run.ScopeId, run.Status,
                             run.CalculationVersion, run.CalculationInputHash, run.ApprovalEvidenceHash,
                             run.CurrencyCode, run.DecimalPrecision, period.Status, period.PeriodEnd,
                             result.Id, result.EmployeeId, result.LocationIdSnapshot, result.NetPay,
                             payment.Id, payment.PaymentBatchId, payment.Status, payment.ResolutionStatus,
                             payment.PayableAmount, payment.PaidAmount, payment.OutstandingAmount,
                             allocation.Id, allocation.AllocationNo, allocation.Method,
                             allocation.Amount, allocation.SettledAmount, allocation.State,
                             evidence.Id, evidence.Type, evidence.EvidenceDate, evidence.EvidenceReference,
                             evidence.PayloadHash, evidence.AllocationEvidenceJson))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var reconciliation = await db.PayrollPaymentReconciliations.AsNoTracking()
            .Where(candidate => candidate.BusinessId == businessId
                && candidate.PayrollPaymentId == paymentId
                && candidate.PayrollPaymentAllocationId == allocationId)
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var allocationAmount = ReadEvidenceAllocationAmount(
            row.AllocationEvidenceJson, row.AllocationNo, row.AllocationMethod);
        return new PayrollAccountingSettlementSource(
            row.BusinessId, row.PayrollPeriodId, row.PayrollRunId, row.RunNo, row.RunType,
            row.RunReference, row.ScopeType, row.ScopeId, row.RunStatus, row.RunRevision,
            row.CalculationInputHash, row.ApprovalEvidenceHash, row.CurrencyCode,
            row.DecimalPrecision, row.PeriodStatus, row.PeriodEnd, row.EmployeeResultId,
            row.EmployeeId, row.HistoricalLocationId, row.NetPay.GetValueOrDefault(),
            row.PaymentId, row.PaymentBatchId, row.PaymentStatus, row.ResolutionStatus,
            row.PayableAmount, row.PaidAmount, row.OutstandingAmount, row.AllocationId,
            row.AllocationNo, row.AllocationMethod, row.AllocationAmount,
            row.AllocationSettledAmount, row.AllocationState, row.SettlementEvidenceId,
            row.EvidenceType, allocationAmount, row.EvidenceDate, row.EvidenceReference, row.EvidencePayloadHash,
            row.AllocationEvidenceJson, reconciliation?.Id, reconciliation?.Version,
            reconciliation?.Status, reconciliation?.ExpectedAmount, reconciliation?.SettledAmount,
            reconciliation?.EvidenceSource);
    }

    public async Task<PayrollAccountingSettlementAccountMappings?> GetSettlementAccountMappingsAsync(
        long businessId, CancellationToken cancellationToken)
    {
        var configuration = await db.AccountingConfigurations.AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .Select(row => new
            {
                row.CashAccountId,
                row.DefaultBankAccountId,
                row.MobileMfsClearingAccountId,
                row.UpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (configuration is null)
            return null;
        var payableMapping = await db.PayrollAccountingComponentMappings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.ComponentType == PayrollAccountingComponentTypes.NetPayable
                && row.ComponentCode == "NET_PAYABLE" && row.IsActive, cancellationToken);
        return new PayrollAccountingSettlementAccountMappings(
            configuration.CashAccountId, configuration.DefaultBankAccountId,
            configuration.MobileMfsClearingAccountId, configuration.UpdatedAt, payableMapping);
    }

    public Task<PayrollAccountingPosting?> FindSettlementPostingAsync(
        long businessId, long evidenceId, long allocationId, CancellationToken cancellationToken) =>
        db.PayrollAccountingPostings.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId
            && row.PostingType == PayrollAccountingPostingTypes.SalaryPayment
            && row.PayrollPaymentEvidenceId == evidenceId
            && row.PayrollPaymentAllocationId == allocationId, cancellationToken);

    public async Task<IReadOnlyList<PayrollAccountingMappingReadModel>> GetMappingsAsync(
        long businessId, CancellationToken cancellationToken) =>
        await (from mapping in db.PayrollAccountingComponentMappings.AsNoTracking()
               join account in db.ChartAccounts.AsNoTracking()
                   on new { mapping.BusinessId, mapping.AccountId }
                   equals new { account.BusinessId, AccountId = account.Id }
               where mapping.BusinessId == businessId
               orderby mapping.ComponentType, mapping.ComponentCode
               select new PayrollAccountingMappingReadModel(
                   mapping.ComponentType, mapping.ComponentCode, mapping.AccountId,
                   account.AccountCode, account.AccountName, mapping.IsActive, mapping.Version))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<PayrollAccountingComponentMapping>> GetMappingRowsAsync(
        long businessId, CancellationToken cancellationToken) =>
        await db.PayrollAccountingComponentMappings.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.IsActive)
            .ToListAsync(cancellationToken);

    public Task<PayrollAccountingComponentMapping?> FindMappingAsync(
        long businessId, string componentType, string componentCode, CancellationToken cancellationToken) =>
        db.PayrollAccountingComponentMappings.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.ComponentType == componentType
            && row.ComponentCode == componentCode, cancellationToken);

    public async Task LockPostingIdentityAsync(
        long businessId, long runId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payroll Accounting posting requires PostgreSQL and an audited transaction.");
        await LockFinancialMutationAsync(businessId, cancellationToken);
        var identity = $"payroll-expense:{businessId}:{runId}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0))", cancellationToken);
    }

    public async Task LockSettlementPostingIdentityAsync(
        long businessId, long evidenceId, long allocationId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll settlement posting identity requires an audited PostgreSQL transaction.");
        await LockFinancialMutationAsync(businessId, cancellationToken);
        var identity = $"payroll:accounting:settlement:{businessId}:{evidenceId}:{allocationId}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0))",
            cancellationToken);
    }

    private async Task LockFinancialMutationAsync(
        long businessId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 13)})",
            cancellationToken);

    public Task<bool?> IsAccountingEnabledAsync(long businessId, CancellationToken cancellationToken) =>
        db.BusinessFeatureSettings.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.FeatureKey == "accounting.enabled")
            .Select(row => (bool?)row.IsEnabled)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> HasUnresolvedCorrectionAsync(
        long businessId, long runId, CancellationToken cancellationToken) =>
        db.PayrollRuns.AsNoTracking().AnyAsync(row =>
            row.BusinessId == businessId && row.OriginalPayrollRunId == runId
            && row.Status != PayrollRunStatuses.Cancelled
            && row.Status != PayrollRunStatuses.Rejected,
            cancellationToken);

    public async Task SavePostingAsync(PayrollAccountingPosting posting, CancellationToken cancellationToken)
    {
        if (posting.Id == 0)
            db.PayrollAccountingPostings.Add(posting);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveMappingAsync(
        PayrollAccountingComponentMapping mapping, CancellationToken cancellationToken)
    {
        if (mapping.Id == 0)
            db.PayrollAccountingComponentMappings.Add(mapping);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryMarkPeriodPostedAsync(
        long businessId, long periodId, long actorUserId, CancellationToken cancellationToken)
    {
        var period = await db.PayrollPeriods.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == periodId, cancellationToken);
        if (period is null)
            return false;
        if (period.Status == PayrollPeriodStatuses.Posted)
            return true;
        if (period.Status != PayrollPeriodStatuses.Approved
            || await IsAccountingEnabledAsync(businessId, cancellationToken) != true)
            return false;

        var runs = await db.PayrollRuns.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollPeriodId == periodId)
            .ToArrayAsync(cancellationToken);
        if (runs.Length == 0 || runs.Any(run => run.Status is not
                (PayrollRunStatuses.Approved or PayrollRunStatuses.PaymentInProgress
                    or PayrollRunStatuses.Paid or PayrollRunStatuses.Completed)))
            return false;

        var postingRunIds = runs.Select(run => run.Id).ToList();
        var postings = await db.PayrollAccountingPostings.AsNoTracking()
            .Where(row => row.BusinessId == businessId && postingRunIds.Contains(row.PayrollRunId))
            .ToArrayAsync(cancellationToken);
        if (runs.Any(run => !postings.Any(posting =>
                posting.PayrollRunId == run.Id
                && posting.RunRevision == run.CalculationVersion
                && posting.Status == PayrollAccountingPostingStatuses.Posted)))
            return false;

        var payableResults = await db.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.BusinessId == businessId && postingRunIds.Contains(row.PayrollRunId)
                && row.NetPay > 0m)
            .ToArrayAsync(cancellationToken);
        if (payableResults.Length > 0)
        {
            var resultIds = payableResults.Select(row => row.Id).ToList();
            var payments = await db.PayrollPayments.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && row.PayrollEmployeeResultId != null
                    && resultIds.Contains(row.PayrollEmployeeResultId.Value)
                    && row.Status != PayrollPaymentStatuses.Cancelled
                    && row.ResolutionStatus != PayrollPaymentResolutionStatuses.ResolvedReissue)
                .Include(row => row.Allocations)
                .Include(row => row.Evidence)
                .ToArrayAsync(cancellationToken);
            var allocationIds = payments.SelectMany(row => row.Allocations)
                .Select(row => row.Id).Distinct().ToList();
            var reconciliationRows = allocationIds.Count == 0
                ? []
                : await db.PayrollPaymentReconciliations.AsNoTracking()
                    .Where(row => row.BusinessId == businessId
                        && allocationIds.Contains(row.PayrollPaymentAllocationId))
                    .OrderBy(row => row.PayrollPaymentAllocationId)
                    .ThenByDescending(row => row.Version)
                    .ThenByDescending(row => row.Id)
                    .ToArrayAsync(cancellationToken);
            var latestReconciliation = reconciliationRows
                .GroupBy(row => row.PayrollPaymentAllocationId)
                .ToDictionary(group => group.Key, group => group.First());
            if (payableResults.Any(result =>
                {
                    var employeePayments = payments.Where(payment =>
                        payment.PayrollEmployeeResultId == result.Id).ToArray();
                    return employeePayments.Length == 0
                        || employeePayments.Sum(payment => payment.PaidAmount) != result.NetPay
                        || employeePayments.Any(payment => payment.Status != PayrollPaymentStatuses.Paid
                            || payment.OutstandingAmount != 0m
                            || payment.Allocations.Any(allocation =>
                                allocation.SettledAmount != allocation.Amount
                                || !latestReconciliation.TryGetValue(allocation.Id, out var reconciliation)
                                || reconciliation.Status != PayrollPaymentReconciliationStatuses.Reconciled
                                || reconciliation.ExpectedAmount != allocation.Amount
                                || reconciliation.SettledAmount != allocation.SettledAmount));
                }))
                return false;
            var settlementIdentities = GetSettlementEvidenceIdentities(payments);
            if (settlementIdentities.Count == 0
                || settlementIdentities.Any(identity => !postings.Any(posting =>
                    posting.PostingType == PayrollAccountingPostingTypes.SalaryPayment
                    && posting.PayrollPaymentEvidenceId == identity.EvidenceId
                    && posting.PayrollPaymentAllocationId == identity.AllocationId
                    && posting.Status == PayrollAccountingPostingStatuses.Posted
                    && posting.AccountingJournalId.HasValue
                    && !string.IsNullOrWhiteSpace(posting.RequestPayloadHash))))
                return false;
        }

        var now = DateTime.UtcNow;
        period.Status = PayrollPeriodStatuses.Posted;
        period.PostedAtUtc = now;
        period.PostedByUserId = actorUserId;
        period.Version = checked(period.Version + 1);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public void DiscardPendingChanges() => db.ChangeTracker.Clear();

    private static string Hash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static decimal ReadEvidenceAllocationAmount(string json, int allocationNo, string method)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var allocation in document.RootElement.EnumerateArray())
        {
            if (allocation.GetProperty("AllocationNo").GetInt32() == allocationNo
                && StringComparer.Ordinal.Equals(allocation.GetProperty("Method").GetString(), method))
                return allocation.GetProperty("Amount").GetDecimal();
        }
        throw new InvalidDataException(
            "Settlement evidence does not contain the requested Payroll payment allocation.");
    }

    private static IReadOnlyList<(long EvidenceId, long AllocationId)> GetSettlementEvidenceIdentities(
        IEnumerable<PayrollPayment> payments)
    {
        var identities = new List<(long EvidenceId, long AllocationId)>();
        foreach (var payment in payments)
        {
            foreach (var evidence in payment.Evidence.Where(row =>
                         row.Type == PayrollPaymentEvidenceTypes.Settlement))
            {
                using var document = JsonDocument.Parse(evidence.AllocationEvidenceJson);
                foreach (var line in document.RootElement.EnumerateArray())
                {
                    var allocationNo = line.GetProperty("AllocationNo").GetInt32();
                    var method = line.GetProperty("Method").GetString();
                    var amount = line.GetProperty("Amount").GetDecimal();
                    var allocation = payment.Allocations.SingleOrDefault(row =>
                        row.AllocationNo == allocationNo && row.Method == method);
                    if (allocation is null || amount <= 0m)
                        throw new InvalidDataException(
                            "Payroll settlement evidence references an invalid allocation.");
                    identities.Add((evidence.Id, allocation.Id));
                }
            }
        }
        return identities;
    }

    private sealed record PayrollAccountingSourceRow(
        long BusinessId, long PeriodId, long Id, int RunNo, string RunType, string? RunReference,
        string? CorrectionReference, long? OriginalRunId, string ScopeType, long? ScopeId,
        string Status, int Revision, string? CalculationHash, string? ApprovalHash, string? Currency,
        int? Precision, string PeriodStatus, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly? PayDate);

    private sealed record PayrollAccountingResultRow(
        long Id, long EmployeeId, long? LocationId, string? SnapshotJson, string? SnapshotHash,
        decimal? Gross, decimal? Deductions, decimal? Net, string Status);

    private sealed record PayrollAccountingLineRow(
        long ResultId, long Id, string ComponentCode, string LineType, decimal Amount);

    private sealed record PayrollAccountingSettlementSourceRow(
        long BusinessId,
        long PayrollPeriodId,
        long PayrollRunId,
        int RunNo,
        string RunType,
        string? RunReference,
        string ScopeType,
        long? ScopeId,
        string RunStatus,
        int RunRevision,
        string? CalculationInputHash,
        string? ApprovalEvidenceHash,
        string? CurrencyCode,
        int? DecimalPrecision,
        string PeriodStatus,
        DateOnly PeriodEnd,
        long EmployeeResultId,
        long EmployeeId,
        long? HistoricalLocationId,
        decimal? NetPay,
        long PaymentId,
        long? PaymentBatchId,
        string PaymentStatus,
        string ResolutionStatus,
        decimal PayableAmount,
        decimal PaidAmount,
        decimal OutstandingAmount,
        long AllocationId,
        int AllocationNo,
        string AllocationMethod,
        decimal AllocationAmount,
        decimal AllocationSettledAmount,
        string AllocationState,
        long SettlementEvidenceId,
        string EvidenceType,
        DateOnly EvidenceDate,
        string EvidenceReference,
        string EvidencePayloadHash,
        string AllocationEvidenceJson);
}
