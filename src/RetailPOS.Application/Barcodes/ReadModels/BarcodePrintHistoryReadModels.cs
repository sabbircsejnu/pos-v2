namespace RetailPOS.Application.Barcodes.ReadModels;

public sealed record BarcodePrintHistorySearchCriteria(
    long? TemplateId,
    long? PrintedByUserId,
    DateTime? StartDate,
    DateTime? EndDate,
    int PageNumber,
    int PageSize);

public sealed record BarcodePrintHistoryItemReadModel(
    long Id,
    long VariantId,
    string ProductName,
    string VariantName,
    string VariantSku,
    string? BarcodeValue,
    string? VariantAttributes,
    decimal? SellingPrice,
    int QuantityPrinted);

public sealed record BarcodePrintHistoryReadModel(
    long Id,
    long? TemplateId,
    string? TemplateName,
    long? OutletId,
    string? OutletName,
    long? PrintedByUserId,
    string? PrintedByUserName,
    string PrintMode,
    decimal LabelWidthMm,
    decimal LabelHeightMm,
    int TotalLabels,
    string? SourceModule,
    string? SourceReferenceType,
    long? SourceReferenceId,
    DateTime PrintedAt,
    IReadOnlyList<BarcodePrintHistoryItemReadModel> Items);

public sealed record BarcodePrintHistorySearchReadModel(
    IReadOnlyList<BarcodePrintHistoryReadModel> Rows,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record BarcodePrintHistoryAccessScope(long? BusinessId, bool IsSuperAdmin);

public enum BarcodePrintHistoryLookupStatus
{
    Found,
    NotFound,
    BusinessScopeRequired,
    Forbidden
}

public sealed record BarcodePrintHistoryLookupReadModel(
    BarcodePrintHistoryLookupStatus Status,
    BarcodePrintHistoryReadModel? History);
