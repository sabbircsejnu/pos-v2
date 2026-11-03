namespace RetailPOS.Application.Barcodes.WriteModels;

public sealed record BarcodeTemplateFieldWriteModel(
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

public sealed record BarcodeTemplateWriteModel(
    string Name,
    string TemplateType,
    string PaperType,
    decimal LabelWidthMm,
    decimal LabelHeightMm,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<BarcodeTemplateFieldWriteModel> Fields);
