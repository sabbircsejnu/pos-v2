using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Sale;

public class CreateSaleDto
{
    [Required]
    public long OutletId { get; set; }

    public long? TerminalId { get; set; }

    public long? CustomerId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Sale must have at least one item")]
    public List<CreateSaleItemDto> Items { get; set; } = new();

    public decimal Discount { get; set; } = 0;

    /// <summary>The VAT the client displayed. The server calculates tax itself and rejects the sale when this differs.</summary>
    public decimal Tax { get; set; } = 0;

    // UPDATED — single convenience field kept for backward compat (single-method sales).
    // When Payments is populated, PaymentMethod is derived from the first entry.
    [Required]
    public string PaymentMethod { get; set; } = "cash";

    // UPDATED — supply one or more payment records for split-payment checkout.
    // If empty the service creates a single SalePayment from PaymentMethod + TotalAmount.
    public List<CreateSalePaymentDto> Payments { get; set; } = new();

    /// <summary>Cash handed over by the customer for a single-method cash sale (required when PaymentMethod is cash and Payments is empty).</summary>
    public decimal? CashTendered { get; set; }

    /// <summary>
    /// Deprecated and never trusted. The cashier is the authenticated user; a value that differs from it is rejected.
    /// </summary>
    public long? CashierId { get; set; }

    /// <summary>
    /// Optional linkage for exchange-style transactions. When supplied, the new sale is treated as a replacement for the original sale.
    /// </summary>
    public long? OriginalSaleId { get; set; }

    /// <summary>Optional eligible outlet user credited with the sale (Phase 4). Validated by the server; never the cashier identity.</summary>
    public long? SalespersonUserId { get; set; }

    /// <summary>
    /// Optional requested sales order date/time.
    /// When omitted, server uses current UTC time.
    /// Backdated values require sales.backdate permission.
    /// </summary>
    public DateTime? SalesDate { get; set; }

    // UPDATED — client-generated UUID (v4) used to prevent duplicate submissions.
    // The server rejects a second request with the same OutletId+IdempotencyKey
    // and returns the original sale instead of creating a second one.
    [Required]
    [MaxLength(64)]
    public string? IdempotencyKey { get; set; }
}

// UPDATED — split payment line
public class CreateSalePaymentDto
{
    [Required]
    public string Method { get; set; } = "cash";

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Payment amount must be positive")]
    public decimal Amount { get; set; }

    /// <summary>Cash tendered (for cash payments only — gives change = Tendered – Amount).</summary>
    public decimal? Tendered { get; set; }
}

