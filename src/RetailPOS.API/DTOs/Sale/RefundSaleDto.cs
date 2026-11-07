namespace RetailPOS.API.DTOs.Sale;

public class RefundSaleDto
{
    public List<RefundItemDto> Items { get; set; } = new();
    public string? Reason { get; set; }

    /// <summary>The terminal whose open cash session pays back the cash (D1); optional when it can be inferred.</summary>
    public long? TerminalId { get; set; }

    /// <summary>The cash actually paid back; required only for a partial refund of a sale paid in cash.</summary>
    public decimal? CashRefundAmount { get; set; }

    /// <summary>
    /// One value per refund attempt, repeated when retrying it. Required from a sale's second refund onwards, so that a
    /// retry is never taken for a new refund.
    /// </summary>
    [System.ComponentModel.DataAnnotations.StringLength(120)]
    public string? IdempotencyKey { get; set; }
}

public class SaleRefundDto
{
    public long Id { get; set; }
    public string RefundNumber { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? Reason { get; set; }
    public long ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal CashAmount { get; set; }
    public List<SaleRefundLineDto> Lines { get; set; } = new();
    public List<SaleRefundPaymentDto> Payments { get; set; } = new();
}

public class SaleRefundLineDto
{
    public long SaleItemId { get; set; }
    public decimal Quantity { get; set; }
    public string Condition { get; set; } = string.Empty;
    public decimal RestockedQuantity { get; set; }
    public bool PackagingReusable { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? VatAmount { get; set; }
}

public class SaleRefundPaymentDto
{
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class RefundItemDto
{
    public long VariantId { get; set; }
    public decimal Quantity { get; set; }
    public long? SaleItemId { get; set; }
    public string? Condition { get; set; }
    public bool PackagingReusable { get; set; }
}
