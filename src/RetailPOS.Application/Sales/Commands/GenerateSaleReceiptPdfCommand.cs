using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Commands;

public sealed record GenerateSaleReceiptPdfCommand(
    long SaleId,
    string? OutputMode,
    long? ActorUserId) : ICommand<SaleReceiptPdfReadModel>;

public sealed class GenerateSaleReceiptPdfCommandHandler(
    ISaleReadStore saleReadStore,
    ISaleReceiptSettingsProvider settingsProvider,
    ISaleReceiptPdfRenderer pdfRenderer,
    ISaleReceiptHistoryWriter historyWriter,
    ISaleReceiptActionAuditWriter auditWriter)
    : IRequestHandler<GenerateSaleReceiptPdfCommand, SaleReceiptPdfReadModel>
{
    public async Task<SaleReceiptPdfReadModel> Handle(
        GenerateSaleReceiptPdfCommand request,
        CancellationToken cancellationToken)
    {
        var sale = await saleReadStore.FindForReceiptAsync(request.SaleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");
        var settings = await settingsProvider.GetAsync(cancellationToken);
        var document = GenerateSaleReceiptDocumentCommandHandler.ComposeDocument(
            sale,
            settings,
            request.OutputMode);
        var bytes = pdfRenderer.Render(document);

        await historyWriter.RecordAsync(
            sale.Id,
            sale.TerminalId,
            request.ActorUserId,
            "saved_pdf",
            DateTime.UtcNow,
            cancellationToken);

        await auditWriter.RecordBestEffortAsync(
            new SaleReceiptActionAuditRecord(
                "ReceiptSavedAsPdf",
                $"Receipt saved_pdf for {sale.SaleNumber}",
                sale.Id,
                sale.SaleNumber,
                sale.OutletId,
                sale.TerminalId,
                document.OutputMode,
                "saved_pdf"),
            cancellationToken);

        return new SaleReceiptPdfReadModel(sale.SaleNumber, bytes);
    }
}
