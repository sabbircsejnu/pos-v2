using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Abstractions;

public interface ISaleReceiptPdfRenderer
{
    byte[] Render(SaleReceiptDocumentReadModel document);
}
