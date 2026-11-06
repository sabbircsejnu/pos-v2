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

    /// <summary>Grand total in words, e.g. "One Thousand Two Hundred Ninety Taka Only".</summary>
    public string? AmountInWords { get; init; }

    /// <summary>Payment rows of the thermal invoice: only the methods with a recorded amount above zero.</summary>
    public IReadOnlyList<SaleReceiptPaymentLineReadModel> PaymentLines { get; init; } = [];
}

public sealed record SaleReceiptPaymentLineReadModel(string Label, decimal Amount);

public sealed record SaleReceiptPaymentReadModel(string Method, decimal Amount, decimal? Tendered, decimal Change);

public sealed record SaleReceiptHeaderReadModel(
    bool ShowLogo,
    string? LogoBase64,
    string? BusinessName,
    string? OutletName,
    string? OutletAddress,
    string? Phone,
    string? BinOrVatNo,
    string? HeaderText)
{
    public string? ConcernOf { get; init; }

    /// <summary>Invoice print branch name, or the outlet name when none is configured.</summary>
    public string? BranchName { get; init; }
    public string? ShowroomHotline { get; init; }
    public string? CentralHotline { get; init; }
    public string? Website { get; init; }

    /// <summary>The boxed identity line, e.g. "BIN: 0029807530203, Mushak-6.3".</summary>
    public string? TaxIdentityLine { get; init; }
}

public sealed record SaleReceiptSaleInfoReadModel(
    string InvoiceNumber,
    DateTime SalesDateTime,
    string Cashier,
    string Terminal,
    string CustomerName,
    string? Salesperson = null,
    DateTime? BusinessDate = null)
{
    public string? CustomerMobile { get; init; }

    /// <summary>The sale time in the outlet's effective time zone.</summary>
    public DateTime? LocalSalesDateTime { get; init; }

    /// <summary>The sale time as printed on the thermal invoice, e.g. "05-Nov-2026 04:20 PM".</summary>
    public string? InvoiceDateText { get; init; }
}

public sealed record SaleReceiptLineItemReadModel(
    string ProductName,
    string VariantLabel,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal LineTotal,
    decimal? VatRate = null,
    decimal? VatAmount = null)
{
    /// <summary>Name, variant and code as printed in the invoice's item column, e.g. "Orna Hijab (ABA1010P)".</summary>
    public string? PrintName { get; init; }

    /// <summary>Line discount as a percentage of quantity x unit price, for display only.</summary>
    public decimal DiscountPercent { get; init; }
}

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
    DateTime GeneratedAt)
{
    public string? VatLawNotification { get; init; }

    /// <summary>The platform-controlled footer line; null when the Business has it disabled or empty.</summary>
    public string? PoweredByText { get; init; }
}

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
    public string? ConcernOf { get; init; }
    public string? Website { get; init; }
    public string? CurrencyCode { get; init; }
    public string? CurrencyName { get; init; }
}
