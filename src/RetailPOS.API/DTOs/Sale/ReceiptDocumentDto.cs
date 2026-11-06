namespace RetailPOS.API.DTOs.Sale;

public sealed class ReceiptDocumentDto
{
    public string OutputMode { get; set; } = "80mm";
    public ReceiptHeaderDto Header { get; set; } = new();
    public ReceiptSaleInfoDto SaleInfo { get; set; } = new();
    public List<ReceiptLineItemDto> Items { get; set; } = new();
    public ReceiptSummaryDto Summary { get; set; } = new();
    public ReceiptFooterDto Footer { get; set; } = new();
    public List<ReceiptPaymentDto> Payments { get; set; } = new();
    public bool TaxInclusive { get; set; }
    public string? AmountInWords { get; set; }
    public List<ReceiptPaymentLineDto> PaymentLines { get; set; } = new();
}

public sealed class ReceiptPaymentLineDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class ReceiptPaymentDto
{
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? Tendered { get; set; }
    public decimal Change { get; set; }
}

public sealed class ReceiptHeaderDto
{
    public bool ShowLogo { get; set; }
    public string? LogoBase64 { get; set; }
    public string? BusinessName { get; set; }
    public string? OutletName { get; set; }
    public string? OutletAddress { get; set; }
    public string? Phone { get; set; }
    public string? BinOrVatNo { get; set; }
    public string? HeaderText { get; set; }
    public string? ConcernOf { get; set; }
    public string? BranchName { get; set; }
    public string? ShowroomHotline { get; set; }
    public string? CentralHotline { get; set; }
    public string? Website { get; set; }
    public string? TaxIdentityLine { get; set; }
}

public sealed class ReceiptSaleInfoDto
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime SalesDateTime { get; set; }
    public string Cashier { get; set; } = string.Empty;
    public string Terminal { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Salesperson { get; set; }
    public DateTime? BusinessDate { get; set; }
    public string? CustomerMobile { get; set; }
    public string? InvoiceDateText { get; set; }
}

public sealed class ReceiptLineItemDto
{
    public string ProductName { get; set; } = string.Empty;
    public string VariantLabel { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal LineTotal { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public string? PrintName { get; set; }
    public decimal DiscountPercent { get; set; }
}

public sealed class ReceiptSummaryDto
{
    public int ItemCount { get; set; }
    public decimal Quantity { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal PaidAmount { get; set; }
    public decimal Change { get; set; }
}

public sealed class ReceiptFooterDto
{
    public string? ThankYouMessage { get; set; }
    public string? ReturnPolicy { get; set; }
    public string? FooterText { get; set; }
    public bool ShowBarcode { get; set; }
    public bool ShowQr { get; set; }
    public string? CodeValue { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string? VatLawNotification { get; set; }
    public string? PoweredByText { get; set; }
}

public sealed class ReceiptPrintAttemptDto
{
    public string Action { get; set; } = "PrintFailed";
    public string? OutputMode { get; set; }
    public string? Message { get; set; }
}
