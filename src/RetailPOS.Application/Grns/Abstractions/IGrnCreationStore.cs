using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnCreationStore
{
    Task<GrnCreationPurchaseOrderReadModel?> FindPurchaseOrderAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<long, int>> GetCompletedReceivedQuantitiesAsync(
        long purchaseOrderId,
        CancellationToken cancellationToken);

    Task<GrnReadModel> CreateAsync(
        GrnCreationRecord record,
        long? businessId,
        CancellationToken cancellationToken);
}

public interface IGrnCreationAuditWriter
{
    Task RecordCreatedAsync(GrnCreationAuditRecord record, CancellationToken cancellationToken);
}

public sealed record GrnCreationRecord(
    long PurchaseOrderId,
    DateTime ReceivedDate,
    string Status,
    string? Notes,
    string? SupplierInvoiceNumber,
    DateTime? SupplierInvoiceDate,
    long? CreatedBy,
    IReadOnlyList<GrnCreationItemRecord> Items);

public sealed record GrnCreationItemRecord(
    long PurchaseOrderItemId,
    int ReceivedQuantity,
    decimal UnitCost,
    decimal TaxableAmount,
    decimal VatRate,
    decimal VatAmount,
    string? TaxCode,
    bool? TaxInclusive,
    string? Notes);

public sealed record GrnCreationAuditRecord(
    long GrnId,
    long? BusinessId,
    long LocationId,
    string LocationType,
    long SupplierId,
    long PurchaseOrderId,
    long? ActorUserId,
    string Status,
    DateTime ReceivedDate,
    int ItemCount,
    int ReceivedQuantity,
    decimal TaxableAmount,
    decimal VatAmount);
