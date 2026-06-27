namespace RetailPOS.API.Documents.Pdf;

public enum DocumentPdfDocumentType
{
    PurchaseOrder,
    SalesInvoice,
    PurchaseInvoice,
    SalesReturn,
    PurchaseReturn,
    Grn,
    StockTransfer,
    QuotationEstimate
}

public sealed record DocumentPdfFileResult(byte[] Content, string FileName);

public sealed record DocumentPdfCompanyInfo(
    string CompanyName,
    IReadOnlyList<string> AddressLines,
    string Phone,
    string Email,
    string Website,
    string TaxNumber,
    string LogoBase64);

public sealed record DocumentPdfPartyInfo(
    string Label,
    string Name,
    IReadOnlyList<string> AddressLines,
    string? Contact = null,
    string? Reference = null);

public sealed record DocumentPdfLineItem(
    int LineNo,
    string ItemName,
    string? VariantName,
    string? Sku,
    string? ProductCode,
    string? Description,
    decimal Quantity,
    string? Unit,
    decimal? UnitRate,
    decimal? Discount,
    decimal? Tax,
    decimal? LineTotal);

public sealed record DocumentPdfSummary(
    decimal Subtotal,
    decimal Discount,
    decimal Tax,
    decimal GrandTotal);

public sealed record DocumentPdfSignature(
    string Label,
    string? Value);

public sealed record DocumentPdfFormattingOptions(
    string CurrencySymbol,
    string CurrencyCode,
    int DecimalPlaces,
    string ThousandsSeparator,
    string DecimalSeparator,
    string SymbolPosition,
    string DateFormat,
    string TimeZone);

public sealed record DocumentPdfRequest(
    DocumentPdfDocumentType DocumentType,
    string Title,
    string DocumentNumber,
    DateTime DocumentDate,
    DocumentPdfCompanyInfo Company,
    DocumentPdfFormattingOptions Formatting,
    DocumentPdfPartyInfo? BillTo,
    DocumentPdfPartyInfo? ShipTo,
    IReadOnlyList<DocumentPdfLineItem> Items,
    DocumentPdfSummary Summary,
    bool ShowCostFields,
    string? Notes = null,
    string? ReferenceNumber = null,
    string? Status = null,
    IReadOnlyList<DocumentPdfSignature>? Signatures = null);