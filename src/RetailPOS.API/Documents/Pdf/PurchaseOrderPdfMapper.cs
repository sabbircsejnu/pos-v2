using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.API.DTOs.Supplier;
using RetailPOS.API.Settings;

namespace RetailPOS.API.Documents.Pdf;

public interface IPurchaseOrderPdfMapper
{
    DocumentPdfRequest Map(PurchaseOrderDto purchaseOrder, SupplierDto supplier, SystemSettings settings, bool showCostFields);
}

public sealed class PurchaseOrderPdfMapper : IPurchaseOrderPdfMapper
{
    public DocumentPdfRequest Map(PurchaseOrderDto purchaseOrder, SupplierDto supplier, SystemSettings settings, bool showCostFields)
    {
        var formatting = new DocumentPdfFormattingOptions(
            CurrencySymbol: settings.Currency.CurrencySymbol,
            CurrencyCode: settings.Currency.CurrencyCode,
            DecimalPlaces: settings.Currency.DecimalPlaces,
            ThousandsSeparator: settings.Currency.ThousandsSeparator,
            DecimalSeparator: settings.Currency.DecimalSeparator,
            SymbolPosition: settings.Currency.SymbolPosition,
            DateFormat: settings.Company.DateFormat,
            TimeZone: settings.Company.TimeZone);

        var items = purchaseOrder.Items.Select((item, index) => new DocumentPdfLineItem(
            LineNo: index + 1,
            ItemName: item.ProductName,
            VariantName: item.VariantName,
            Sku: item.Sku,
            ProductCode: item.ProductCode,
            Description: item.VariantAttributes,
            Quantity: item.Quantity,
            Unit: item.Unit,
            UnitRate: item.UnitPrice,
            Discount: item.Discount,
            Tax: item.Tax,
            LineTotal: item.TotalPrice)).ToList();

        var subtotal = items.Sum(item => (item.UnitRate ?? 0m) * item.Quantity);
        var discount = items.Sum(item => (item.UnitRate ?? 0m) * item.Quantity * ((item.Discount ?? 0m) / 100m));
        var taxBase = subtotal - discount;
        var tax = items.Sum(item => ((item.UnitRate ?? 0m) * item.Quantity * (1 - ((item.Discount ?? 0m) / 100m))) * ((item.Tax ?? 0m) / 100m));
        var grandTotal = items.Sum(item => item.LineTotal ?? 0m);

        return new DocumentPdfRequest(
            DocumentType: DocumentPdfDocumentType.PurchaseOrder,
            Title: "Purchase Order",
            DocumentNumber: purchaseOrder.PoNumber,
            DocumentDate: purchaseOrder.OrderDate,
            Company: new DocumentPdfCompanyInfo(
                CompanyName: settings.Company.CompanyName,
                AddressLines: DocumentPdfFormatting.SplitLines(settings.Company.Address),
                Phone: settings.Company.Phone,
                Email: settings.Company.Email,
                Website: settings.Company.Website,
                TaxNumber: settings.Company.TaxNumber,
                LogoBase64: settings.Company.LogoBase64),
            Formatting: formatting,
            BillTo: new DocumentPdfPartyInfo(
                Label: "Vendor",
                Name: supplier.Name,
                AddressLines: DocumentPdfFormatting.SplitLines(supplier.Address),
                Contact: supplier.Contact),
            ShipTo: new DocumentPdfPartyInfo(
                Label: "Ship To",
                Name: purchaseOrder.WarehouseName,
                AddressLines: Array.Empty<string>()),
            Items: items,
            Summary: new DocumentPdfSummary(
                Subtotal: subtotal,
                Discount: discount,
                Tax: tax,
                GrandTotal: grandTotal),
            ShowCostFields: showCostFields,
            Notes: purchaseOrder.Notes,
            Status: purchaseOrder.Status,
            Signatures: BuildSignatures(purchaseOrder));
    }

    private static IReadOnlyList<DocumentPdfSignature> BuildSignatures(PurchaseOrderDto purchaseOrder)
    {
        var signatures = new List<DocumentPdfSignature>();

        if (!string.IsNullOrWhiteSpace(purchaseOrder.CreatedByName))
        {
            signatures.Add(new DocumentPdfSignature("Prepared By", purchaseOrder.CreatedByName));
        }

        signatures.Add(new DocumentPdfSignature("Approved By", string.Empty));

        return signatures;
    }
}