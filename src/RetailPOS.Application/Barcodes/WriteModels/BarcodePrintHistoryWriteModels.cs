namespace RetailPOS.Application.Barcodes.WriteModels;

public sealed record BarcodePrintHistoryItemWriteModel(
    long VariantId,
    string ProductName,
    string VariantName,
    string VariantSku,
    string? BarcodeValue,
    string? VariantAttributes,
    decimal? SellingPrice,
    int QuantityPrinted);

public sealed record BarcodePrintHistoryWriteModel(
    long? TemplateId,
    long? OutletId,
    long? PrintedByUserId,
    string PrintMode,
    decimal LabelWidthMm,
    decimal LabelHeightMm,
    string? SourceModule,
    string? SourceReferenceType,
    long? SourceReferenceId,
    IReadOnlyList<BarcodePrintHistoryItemWriteModel> Items);
