using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Application.Barcodes.WriteModels;

namespace RetailPOS.Application.Barcodes.Commands;

public sealed record RecordBarcodePrintHistoryItem(
    long VariantId,
    string ProductName,
    string VariantName,
    string VariantSku,
    string? BarcodeValue,
    string? VariantAttributes,
    decimal? SellingPrice,
    int QuantityPrinted);

public sealed record RecordBarcodePrintHistoryCommand(
    long? TemplateId,
    long? OutletId,
    long? PrintedByUserId,
    string PrintMode,
    decimal LabelWidthMm,
    decimal LabelHeightMm,
    string? SourceModule,
    string? SourceReferenceType,
    long? SourceReferenceId,
    IReadOnlyList<RecordBarcodePrintHistoryItem> Items)
    : ICommand<BarcodePrintHistoryReadModel>;

public sealed class RecordBarcodePrintHistoryCommandHandler(
    IBarcodePrintHistoryWriteStore store,
    IBarcodePrintHistoryWriteScope writeScope)
    : IRequestHandler<RecordBarcodePrintHistoryCommand, BarcodePrintHistoryReadModel>
{
    private static readonly HashSet<string> AllowedPrintModes =
        new(StringComparer.OrdinalIgnoreCase) { "pdf", "browser", "thermal" };

    public async Task<BarcodePrintHistoryReadModel> Handle(
        RecordBarcodePrintHistoryCommand request,
        CancellationToken cancellationToken)
    {
        if (!AllowedPrintModes.Contains(request.PrintMode))
            throw new InvalidOperationException("Invalid print mode. Allowed values: pdf, browser, thermal");

        if (request.Items.Count == 0)
            throw new InvalidOperationException("At least one print history item is required");

        var scope = await writeScope.GetAsync(cancellationToken);
        if (!scope.BusinessId.HasValue)
            throw new RetailPOS.Application.Authorization.BusinessContextRequiredException();

        var writeModel = new BarcodePrintHistoryWriteModel(
            request.TemplateId,
            request.OutletId,
            request.PrintedByUserId,
            request.PrintMode.Trim().ToLowerInvariant(),
            request.LabelWidthMm,
            request.LabelHeightMm,
            request.SourceModule?.Trim(),
            request.SourceReferenceType?.Trim(),
            request.SourceReferenceId,
            request.Items.Select(item => new BarcodePrintHistoryItemWriteModel(
                item.VariantId,
                item.ProductName,
                item.VariantName,
                item.VariantSku,
                item.BarcodeValue,
                item.VariantAttributes,
                item.SellingPrice,
                item.QuantityPrinted)).ToArray());

        return await store.CreateAsync(
            writeModel,
            scope.BusinessId.Value,
            scope.IsSuperAdmin,
            cancellationToken);
    }
}
