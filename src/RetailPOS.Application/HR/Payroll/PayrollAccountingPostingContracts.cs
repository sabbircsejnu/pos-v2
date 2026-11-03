using MediatR;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record PayrollAccountingSource(
    long BusinessId,
    long PayrollPeriodId,
    long PayrollRunId,
    int RunNo,
    string RunType,
    string? RunReference,
    string? CorrectionReference,
    long? OriginalRunId,
    string ScopeType,
    long? ScopeId,
    string RunStatus,
    int Revision,
    string? CalculationInputHash,
    string? ApprovalEvidenceHash,
    string? CurrencyCode,
    int? DecimalPrecision,
    string PeriodStatus,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? PayDate,
    IReadOnlyList<PayrollAccountingEmployeeSource> Employees);

public sealed record PayrollAccountingEmployeeSource(
    long EmployeeResultId,
    long EmployeeId,
    long? HistoricalLocationId,
    string SnapshotJson,
    string SnapshotHash,
    decimal GrossEarnings,
    decimal TotalDeductions,
    decimal NetPay,
    IReadOnlyList<PayrollAccountingComponentLine> Lines);

public sealed record PayrollAccountingComponentLine(
    long ResultLineId,
    string ComponentCode,
    string LineType,
    decimal Amount);

public sealed record PayrollAccountingMappingReadModel(
    string ComponentType,
    string ComponentCode,
    long AccountId,
    string AccountCode,
    string AccountName,
    bool IsActive,
    long Version);

public sealed record PayrollAccountingPostingReadModel(
    long Id,
    long BusinessId,
    long PayrollPeriodId,
    long PayrollRunId,
    int RunRevision,
    string PostingType,
    string PostingReference,
    string IdempotencyKey,
    string? RequestPayloadHash,
    string? ConflictPayloadHash,
    long? AccountingJournalId,
    string? AccountingReference,
    string Status,
    string? ErrorMessage,
    int AttemptCount,
    long Version,
    DateTime? PostedAtUtc);

public sealed record PayrollAccountingPostingResponse(
    PayrollAccountingPostingReadModel? Posting,
    string? Message = null);

public sealed record PayrollAccountingSettlementSource(
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
    decimal NetPay,
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
    decimal EvidenceAmount,
    DateOnly EvidenceDate,
    string EvidenceReference,
    string EvidencePayloadHash,
    string AllocationEvidenceJson,
    long? ReconciliationId,
    long? ReconciliationVersion,
    string? ReconciliationStatus,
    decimal? ReconciliationExpectedAmount,
    decimal? ReconciliationSettledAmount,
    string? ReconciliationEvidenceSource);

public sealed record PayrollAccountingSettlementAccountMappings(
    long? CashAccountId,
    long? DefaultBankAccountId,
    long? MobileMfsClearingAccountId,
    DateTime UpdatedAt,
    PayrollAccountingComponentMapping? NetPayableMapping);

public enum PayrollAccountingCorrectionStrategy
{
    None,
    DifferentialAdjustment,
    ReversalAndReplacement
}

public sealed record PayrollAccountingCorrectionPolicy(
    PayrollAccountingCorrectionStrategy Strategy,
    string Version);

public interface IPayrollAccountingCorrectionPolicy
{
    Task<PayrollAccountingCorrectionPolicy> ResolveAsync(
        long businessId, string correctionReference, CancellationToken cancellationToken);
}

public sealed class UnconfiguredPayrollAccountingCorrectionPolicy
    : IPayrollAccountingCorrectionPolicy
{
    public Task<PayrollAccountingCorrectionPolicy> ResolveAsync(
        long businessId, string correctionReference, CancellationToken cancellationToken) =>
        Task.FromResult(new PayrollAccountingCorrectionPolicy(
            PayrollAccountingCorrectionStrategy.None, "unconfigured"));
}

public interface IPayrollAccountingPostingStore
{
    Task<PayrollAccountingSource?> FindSourceAsync(
        long businessId, long runId, CancellationToken cancellationToken);

    Task<PayrollAccountingPosting?> FindPostingAsync(
        long businessId, long runId, int revision, CancellationToken cancellationToken);

    Task<PayrollAccountingPosting?> FindCorrectionPostingAsync(
        long businessId, long runId, int revision, CancellationToken cancellationToken);

    Task<PayrollAccountingPosting?> FindCorrectionByOriginalAsync(
        long businessId, long originalPostingId, CancellationToken cancellationToken);

    Task<PayrollAccountingSettlementSource?> FindSettlementSourceAsync(
        long businessId, long paymentId, long allocationId, long evidenceId,
        CancellationToken cancellationToken);

    Task<PayrollAccountingSettlementAccountMappings?> GetSettlementAccountMappingsAsync(
        long businessId, CancellationToken cancellationToken);

    Task<PayrollAccountingPosting?> FindSettlementPostingAsync(
        long businessId, long evidenceId, long allocationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayrollAccountingMappingReadModel>> GetMappingsAsync(
        long businessId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayrollAccountingComponentMapping>> GetMappingRowsAsync(
        long businessId, CancellationToken cancellationToken);

    Task<PayrollAccountingComponentMapping?> FindMappingAsync(
        long businessId, string componentType, string componentCode, CancellationToken cancellationToken);

    Task LockPostingIdentityAsync(
        long businessId, long runId, CancellationToken cancellationToken);

    Task LockSettlementPostingIdentityAsync(
        long businessId, long evidenceId, long allocationId, CancellationToken cancellationToken);

    Task<bool?> IsAccountingEnabledAsync(long businessId, CancellationToken cancellationToken);

    Task<bool> HasUnresolvedCorrectionAsync(
        long businessId, long runId, CancellationToken cancellationToken);

    Task SavePostingAsync(PayrollAccountingPosting posting, CancellationToken cancellationToken);

    Task SaveMappingAsync(PayrollAccountingComponentMapping mapping, CancellationToken cancellationToken);

    Task<bool> TryMarkPeriodPostedAsync(
        long businessId, long periodId, long actorUserId, CancellationToken cancellationToken);

    void DiscardPendingChanges();
}

public sealed record PostPayrollAccountingExpenseCommand(long RunId)
    : IRequest<PayrollAccountingPostingResponse>;

public sealed record PostPayrollAccountingSettlementCommand(
    long PaymentId, long AllocationId, long EvidenceId)
    : IRequest<PayrollAccountingPostingResponse>;

public sealed record GetPayrollAccountingPostingQuery(long RunId)
    : IRequest<PayrollAccountingPostingResponse>;

public sealed record GetPayrollAccountingMappingsQuery : IRequest<IReadOnlyList<PayrollAccountingMappingReadModel>>;

public sealed record UpsertPayrollAccountingMappingCommand(
    string ComponentType, string ComponentCode, long AccountId)
    : IRequest<PayrollAccountingMappingReadModel>;

public sealed class PayrollAccountingPostingException(string message) : InvalidOperationException(message);

public sealed class PayrollAccountingPostingConflictException(string message) : InvalidOperationException(message);
