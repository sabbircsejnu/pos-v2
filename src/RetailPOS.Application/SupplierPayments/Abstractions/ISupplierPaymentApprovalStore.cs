using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Application.SupplierPayments.Abstractions;

public interface ISupplierPaymentApprovalStore
{
    Task<SupplierPaymentApprovalScope?> FindForApprovalAsync(
        long paymentId,
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierPaymentReadModel> ApproveAsync(
        long paymentId,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken);
}

public interface ISupplierPaymentApprovalJournalPoster
{
    Task<SupplierPaymentApprovalPostingAccounts> ResolvePostingAccountsAsync(
        CancellationToken cancellationToken);

    Task<long> PostPaymentAsync(
        SupplierPaymentApprovalJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<long> PostAdvanceAsync(
        SupplierPaymentApprovalJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken);
}

public sealed record SupplierPaymentApprovalScope(long? OutletId, string Status);

public sealed record SupplierPaymentApprovalPostingAccounts(
    long SupplierPayableAccountId,
    long SupplierAdvanceAccountId);

public sealed record SupplierPaymentApprovalJournalRequest(
    long BusinessId,
    long PaymentId,
    long? OutletId,
    DateTime PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    long PaymentAccountId,
    long OffsetAccountId,
    decimal Amount);

public sealed record SupplierPaymentApprovalAuditRecord(
    long PaymentId,
    long BusinessId,
    long? OutletId,
    long SupplierId,
    decimal Amount,
    string PaymentMethod,
    long PaymentAccountId,
    long? JournalEntryId,
    long? ReversalJournalEntryId,
    decimal AllocatedAmount,
    decimal AdvanceAmount,
    string AllocationSummary,
    long ActorUserId);

public interface ISupplierPaymentApprovalAuditWriter
{
    Task RecordApprovedAsync(
        SupplierPaymentApprovalAuditRecord record,
        CancellationToken cancellationToken);
}
