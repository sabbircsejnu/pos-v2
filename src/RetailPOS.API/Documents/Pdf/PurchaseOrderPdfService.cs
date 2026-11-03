using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.API.DTOs.Supplier;
using RetailPOS.Application.PurchaseOrders.Queries;
using RetailPOS.Application.Suppliers.Queries;
using RetailPOS.Application.Settings.Queries;
using MediatR;

namespace RetailPOS.API.Documents.Pdf;

public interface IPurchaseOrderPdfService
{
    Task<DocumentPdfFileResult> GenerateAsync(long purchaseOrderId, bool showCostFields, CancellationToken cancellationToken = default);
}

public sealed class PurchaseOrderPdfService : IPurchaseOrderPdfService
{
    private readonly ISender _sender;
    private readonly IPurchaseOrderPdfMapper _mapper;
    private readonly IDocumentPdfRenderer _renderer;

    public PurchaseOrderPdfService(
        ISender sender,
        IPurchaseOrderPdfMapper mapper,
        IDocumentPdfRenderer renderer)
    {
        _sender = sender;
        _mapper = mapper;
        _renderer = renderer;
    }

    public async Task<DocumentPdfFileResult> GenerateAsync(long purchaseOrderId, bool showCostFields, CancellationToken cancellationToken = default)
    {
        var purchaseOrder = PurchaseDocumentDtoMapper.MapPurchaseOrder(await _sender.Send(
            new GetPurchaseOrderByIdQuery(purchaseOrderId), cancellationToken));
        var supplierReadModel = await _sender.Send(
            new GetSupplierByIdQuery(purchaseOrder.SupplierId),
            cancellationToken);
        var supplier = new SupplierDto
        {
            Id = supplierReadModel.Id,
            Name = supplierReadModel.Name,
            Contact = supplierReadModel.Contact,
            Address = supplierReadModel.Address,
            CreditLimit = supplierReadModel.CreditLimit,
            CreatedAt = supplierReadModel.CreatedAt,
            UpdatedAt = supplierReadModel.UpdatedAt
        };
        var settings = await _sender.Send(new GetSystemSettingsQuery(), cancellationToken);

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