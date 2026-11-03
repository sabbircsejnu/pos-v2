using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.SupplierPayments.Abstractions;

public interface ISupplierAdvanceCreationStore
{
    Task<bool> SupplierExistsAsync(
        long businessId,
        long supplierId,
        CancellationToken cancellationToken);

    Task<bool> PaymentAccountExistsAsync(
        long businessId,
        long paymentAccountId,
        CancellationToken cancellationToken);

    Task<SupplierAdvanceReadModel> CreateAsync(
        SupplierAdvance advance,
        CancellationToken cancellationToken);
}

public interface ISupplierAdvanceCreationAuditWriter
{
    Task RecordCreatedAsync(
        SupplierAdvanceCreationAuditRecord record,
        CancellationToken cancellationToken);
}

public sealed record SupplierAdvanceCreationAuditRecord(
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
