using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.API.DTOs.Supplier;
using RetailPOS.API.Services;

namespace RetailPOS.API.Documents.Pdf;

public interface IPurchaseOrderPdfService
{
    Task<DocumentPdfFileResult> GenerateAsync(long purchaseOrderId, bool showCostFields, CancellationToken cancellationToken = default);
}

public sealed class PurchaseOrderPdfService : IPurchaseOrderPdfService
{
    private readonly IPurchaseOrderService _purchaseOrderService;
    private readonly ISupplierService _supplierService;
    private readonly ISettingsService _settingsService;
    private readonly IPurchaseOrderPdfMapper _mapper;
    private readonly IDocumentPdfRenderer _renderer;

    public PurchaseOrderPdfService(
        IPurchaseOrderService purchaseOrderService,
        ISupplierService supplierService,
        ISettingsService settingsService,
        IPurchaseOrderPdfMapper mapper,
        IDocumentPdfRenderer renderer)
    {
        _purchaseOrderService = purchaseOrderService;
        _supplierService = supplierService;
        _settingsService = settingsService;
        _mapper = mapper;
        _renderer = renderer;
    }

    public async Task<DocumentPdfFileResult> GenerateAsync(long purchaseOrderId, bool showCostFields, CancellationToken cancellationToken = default)
    {
        var purchaseOrder = await _purchaseOrderService.GetByIdAsync(purchaseOrderId);
        var supplier = await _supplierService.GetSupplierByIdAsync(purchaseOrder.SupplierId);
        var settings = await _settingsService.GetAllSettingsAsync();

        var request = _mapper.Map(purchaseOrder, supplier, settings, showCostFields);
        var pdfBytes = await _renderer.RenderAsync(request, cancellationToken);

        var fileName = DocumentPdfFileNameBuilder.Build(purchaseOrder.PoNumber, $"PO-{purchaseOrder.Id:D6}");
        return new DocumentPdfFileResult(pdfBytes, fileName);
    }
}

public static class DocumentPdfFileNameBuilder
{
    public static string Build(string? value, string fallback)
    {
        var fileBase = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = new string(fileBase.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return $"{safeName}.pdf";
    }
}