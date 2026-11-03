namespace RetailPOS.Application.Barcodes.ReadModels;

public sealed record BarcodeTemplateFieldReadModel(
    long Id,
    string FieldKey,
    bool IsEnabled,
    int SortOrder,
    decimal? X,
    decimal? Y,
    decimal? Width,
    decimal? Height,
    decimal? FontSize,
    string? FontWeight,
    string? Align);

public sealed record BarcodeTemplateReadModel(
    long Id,
    string Name,
    string TemplateType,
    string PaperType,
    decimal LabelWidthMm,
    decimal LabelHeightMm,
    bool IsDefault,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<BarcodeTemplateFieldReadModel> Fields);

public sealed record BarcodeTemplateAccessScope(long? BusinessId, bool IsSuperAdmin);

public enum BarcodeTemplateLookupStatus
{
    Found,
    NotFound,
    BusinessScopeRequired,
    Forbidden
}

public sealed record BarcodeTemplateLookupReadModel(
    BarcodeTemplateLookupStatus Status,
    BarcodeTemplateReadModel? Template);
