using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Application.SupplierPayments.Abstractions;

public interface ISupplierAdvanceSubmissionStore
{
    Task<SupplierAdvanceSubmissionSnapshot?> FindForSubmissionAsync(
        long advanceId,
        long businessId,
        CancellationToken cancellationToken);

    Task SubmitAsync(
        long advanceId,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<SupplierAdvanceReadModel> FindByIdAsync(
        long advanceId,
        CancellationToken cancellationToken);
}

public interface ISupplierAdvanceSubmissionAuditWriter
{
    Task RecordSubmittedAsync(
        SupplierAdvanceSubmissionAuditRecord record,
        CancellationToken cancellationToken);
}

public sealed record SupplierAdvanceSubmissionSnapshot(
    long Id,
    long? OutletId,
    string Status,
    long SupplierId,
    decimal Amount,
    decimal RemainingAmount,
    decimal AppliedAmount,
    string PaymentMethod,
    long PaymentAccountId,
    long? JournalEntryId,
    long? SourcePaymentId);

public sealed record SupplierAdvanceSubmissionAuditRecord(
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
