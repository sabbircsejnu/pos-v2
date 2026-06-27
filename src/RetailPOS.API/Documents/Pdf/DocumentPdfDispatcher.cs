namespace RetailPOS.API.Documents.Pdf;

public interface IDocumentPdfDispatcher
{
    Task<DocumentPdfFileResult> GenerateAsync(string documentType, long documentId, bool showCostFields, CancellationToken cancellationToken = default);
}

public sealed class DocumentPdfDispatcher : IDocumentPdfDispatcher
{
    private readonly IPurchaseOrderPdfService _purchaseOrderPdfService;
    private readonly IGrnPdfService _grnPdfService;

    public DocumentPdfDispatcher(IPurchaseOrderPdfService purchaseOrderPdfService, IGrnPdfService grnPdfService)
    {
        _purchaseOrderPdfService = purchaseOrderPdfService;
        _grnPdfService = grnPdfService;
    }

    public Task<DocumentPdfFileResult> GenerateAsync(string documentType, long documentId, bool showCostFields, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            throw new NotSupportedException("Document type is required.");
        }

        return documentType.Trim().ToLowerInvariant() switch
        {
            "purchase-order" or "purchase_order" or "po" => _purchaseOrderPdfService.GenerateAsync(documentId, showCostFields, cancellationToken),
            "grn" or "goods-received-note" or "goods_received_note" => _grnPdfService.GenerateAsync(documentId, showCostFields, cancellationToken),
            _ => throw new NotSupportedException($"Document type '{documentType}' is not implemented yet.")
        };
    }
}