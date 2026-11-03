namespace RetailPOS.Application.SupplierPayments.ReadModels;

public sealed record SupplierPaymentReadModel(
    long Id,
    long SupplierId,
    string SupplierName,
    long? OutletId,
    DateTime PaymentDate,
    DateTime? PostingDate,
    string PaymentMethod,
    long PaymentAccountId,
    decimal Amount,
    decimal AllocatedAmount,
    decimal AdvanceAmount,
    string Status,
    string? ReferenceNumber,
    string? Notes,
    long? JournalEntryId);
