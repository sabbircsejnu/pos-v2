using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Application.SupplierPayments.Abstractions;

public interface ISupplierAdvanceApprovalStore
{
    Task<SupplierAdvanceApprovalSnapshot?> FindForApprovalAsync(
        long advanceId,
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierAdvanceReadModel> ApproveAsync(
        long advanceId,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken);
}

public interface ISupplierAdvanceApprovalJournalPoster
{
    Task<long> ResolveAdvanceAccountIdAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<long> PostAsync(
        SupplierAdvanceApprovalJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken);
}

public sealed record SupplierAdvanceApprovalSnapshot(
    long Id,
    long? OutletId,
    string Status);

public sealed record SupplierAdvanceApprovalJournalRequest(
    long BusinessId,
    long AdvanceId,
    long? OutletId,
    DateTime PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    long AdvanceAccountId,
    long PaymentAccountId,
    decimal Amount);

public sealed record SupplierAdvanceApprovalAuditRecord(
    long AdvanceId,
    long BusinessId,
    long? OutletId,
    long SupplierId,
    decimal Amount,
    decimal RemainingAmount,
    decimal AppliedAmount,
    string PaymentMethod,
    long PaymentAccountId,
    long? JournalEntryId,
    long? SourcePaymentId,
    long ActorUserId);

public interface ISupplierAdvanceApprovalAuditWriter
{
    Task RecordApprovedAsync(
        SupplierAdvanceApprovalAuditRecord record,
        CancellationToken cancellationToken);
}
