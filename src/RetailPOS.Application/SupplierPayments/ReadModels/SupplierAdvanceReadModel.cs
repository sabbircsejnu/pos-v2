namespace RetailPOS.Application.SupplierPayments.ReadModels;

public sealed record SupplierAdvanceReadModel(
    long Id,
    long SupplierId,
    string SupplierName,
    long? OutletId,
    DateTime PaymentDate,
    string PaymentMethod,
    long PaymentAccountId,
    decimal Amount,
    decimal RemainingAmount,
    decimal AppliedAmount,
    string Status,
    string? ReferenceNumber,
    string? Notes,
    long? JournalEntryId);
