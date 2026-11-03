using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record PayrollFinalSettlementAccountingStatus(
    long Id,
    int RevisionNo,
    string PostingType,
    string PostingReference,
    string Status,
    long? AccountingJournalId,
    string? AccountingReference,
    string? ErrorMessage,
    int AttemptCount,
    long Version,
    DateTime? PostedAtUtc);

public sealed record PayrollFinalSettlementExecutionStatus(
    long FinalSettlementId,
    int RevisionNo,
    string SettlementStatus,
    long? ApprovalInstanceId,
    IReadOnlyList<PayrollPaymentResponse> Payments,
    IReadOnlyList<PayrollFinalSettlementAccountingStatus> AccountingPostings);

public interface IPayrollFinalSettlementExecutionStore
{
    Task<IReadOnlyList<PayrollFinalSettlementAccountingStatus>> GetAccountingStatusAsync(
        long businessId, long settlementId, int revisionNo,
        CancellationToken cancellationToken);
    Task<PayrollFinalSettlementAccountingPosting?> FindPostingAsync(
        long businessId, long settlementId, int revisionNo, string postingType,
        long? paymentId, long? paymentEvidenceId, CancellationToken cancellationToken);
    Task LockPostingIdentityAsync(
        long businessId, string postingReference, CancellationToken cancellationToken);
    Task SavePostingAsync(
        PayrollFinalSettlementAccountingPosting posting, CancellationToken cancellationToken);
    Task<PayrollFinalSettlementPaymentAccountingSource?> GetPaymentSourceAsync(
        long businessId, long paymentId, long evidenceId, CancellationToken cancellationToken);
}

public sealed record PayrollFinalSettlementPaymentAllocationSource(
    long AllocationId,
    int AllocationNo,
    string Method,
    decimal Amount,
    decimal SettledAmount,
    string State,
    decimal EvidenceAmount,
    bool IsReconciled);

public sealed record PayrollFinalSettlementPaymentAccountingSource(
    long FinalSettlementId,
    int RevisionNo,
    long RevisionId,
    long EmployeeId,
    long? HistoricalLocationId,
    int DecimalPrecision,
    string RoundingMode,
    string CurrencyCode,
    string SubmittedEvidenceHash,
    long PaymentId,
    string PaymentStatus,
    string ResolutionStatus,
    decimal PayableAmount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    long EvidenceId,
    string EvidenceType,
    DateOnly EvidenceDate,
    string EvidenceReference,
    string EvidencePayloadHash,
    IReadOnlyList<PayrollFinalSettlementPaymentAllocationSource> Allocations);

public sealed record PostPayrollFinalSettlementAccountingCommand(
    long Id, int RevisionNo, string IdempotencyKey)
    : IRequest<PayrollFinalSettlementAccountingStatus>;

public sealed record PostPayrollFinalSettlementPaymentAccountingCommand(
    long PaymentId, long EvidenceId)
    : IRequest<PayrollFinalSettlementAccountingStatus>;

public sealed class PayrollFinalSettlementAccountingException(string message)
    : InvalidOperationException(message);

public sealed class PayrollFinalSettlementAccountingConflictException(string message)
    : InvalidOperationException(message);

public sealed record GetPayrollFinalSettlementExecutionStatusQuery(long Id)
    : IRequest<PayrollFinalSettlementExecutionStatus>;

public sealed class PayrollFinalSettlementExecutionHandlers(
    IPayrollFinalSettlementStore settlements,
    IPayrollPaymentStore payments,
    IPayrollFinalSettlementExecutionStore execution,
    IHrExitAccess access,
    IRequiredAuditTransaction audit) :
    IRequestHandler<GetPayrollFinalSettlementExecutionStatusQuery,
        PayrollFinalSettlementExecutionStatus>
{
    public async Task<PayrollFinalSettlementExecutionStatus> Handle(
        GetPayrollFinalSettlementExecutionStatusQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Id <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.Id));
        var businessId = access.RequireBusinessId();
        var scope = await settlements.GetSettlementScopeAsync(
            businessId, query.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Final Settlement was not found.");
        await access.EnsureAsync(
            businessId, HrExitOperation.FinalSettlementFinancialView,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var settlement = await settlements.GetByIdAsync(
            businessId, query.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Final Settlement was not found.");
        var paymentRows = await payments.GetFinalSettlementPaymentsAsync(
            businessId, query.Id, settlement.RevisionNo, cancellationToken);
        var postingRows = await execution.GetAccountingStatusAsync(
            businessId, query.Id, settlement.RevisionNo, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId,
                "payroll.final_settlement.execution.accessed", "Payroll",
                "Accessed Final Settlement payment and Accounting execution status.",
                "PayrollFinalSettlement", query.Id.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            _ => Task.FromResult(new PayrollFinalSettlementExecutionStatus(
                query.Id, settlement.RevisionNo, settlement.Status,
                settlement.ApprovalInstanceId, paymentRows, postingRows)),
            cancellationToken);
    }
}
