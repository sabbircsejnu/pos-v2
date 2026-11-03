namespace RetailPOS.Application.Sales.ReadModels;

public sealed record SaleReceiptDocumentReadModel(
    string OutputMode,
    SaleReceiptHeaderReadModel Header,
    SaleReceiptSaleInfoReadModel SaleInfo,
    IReadOnlyList<SaleReceiptLineItemReadModel> Items,
    SaleReceiptSummaryReadModel Summary,
    SaleReceiptFooterReadModel Footer)
{
    public string DateFormat { get; init; } = "yyyy-MM-dd";

    /// <summary>Every recorded payment (split sales have several). Cash rows carry tendered and change.</summary>
    public IReadOnlyList<SaleReceiptPaymentReadModel> Payments { get; init; } = [];

    /// <summary>True when the selling prices already include VAT (from the immutable sale line snapshot).</summary>
    public bool TaxInclusive { get; init; }
}

public sealed record SaleReceiptPaymentReadModel(string Method, decimal Amount, decimal? Tendered, decimal Change);

public sealed record SaleReceiptHeaderReadModel(
    bool ShowLogo,
    string? LogoBase64,
    string? BusinessName,
    string? OutletName,
    string? OutletAddress,
    string? Phone,
    string? BinOrVatNo,
    string? HeaderText);

public sealed record SaleReceiptSaleInfoReadModel(
    string InvoiceNumber,
    DateTime SalesDateTime,
    string Cashier,
    string Terminal,
    string CustomerName,
    string? Salesperson = null,
    DateTime? BusinessDate = null);

public sealed record SaleReceiptLineItemReadModel(
    string ProductName,
    string VariantLabel,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal LineTotal,
    decimal? VatRate = null,
    decimal? VatAmount = null);

public sealed record SaleReceiptSummaryReadModel(
    int ItemCount,
    decimal Quantity,
    decimal Subtotal,
    decimal Discount,
    decimal Tax,
    decimal RoundOff,
    decimal GrandTotal,
    string PaymentMethod,
    decimal PaidAmount,
    decimal Change);

public sealed record SaleReceiptFooterReadModel(
    string? ThankYouMessage,
    string? ReturnPolicy,
    string? FooterText,
    bool ShowBarcode,
    bool ShowQr,
    string? CodeValue,
    DateTime GeneratedAt);

public sealed record SaleReceiptSettingsReadModel(
    string? DefaultReceiptSize,
    string? PaperSize,
    bool ShowLogo,
    bool ShowBarcode,
    bool ShowQr,
    string? Header,
    string? ThankYouMessage,
    string? ReturnPolicy,
    string? Footer,
    string? CompanyName,
    string? LogoBase64,
    string? CompanyPhone,
    string? TaxNumber)
{
    public string DateFormat { get; init; } = "yyyy-MM-dd";
}
