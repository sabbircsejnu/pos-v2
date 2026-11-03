namespace RetailPOS.API.DTOs.Sale;

public class RefundSaleDto
{
    public List<RefundItemDto> Items { get; set; } = new();
    public string? Reason { get; set; }

    /// <summary>The terminal whose open cash session pays back the cash (D1); optional when it can be inferred.</summary>
    public long? TerminalId { get; set; }

    /// <summary>The cash actually paid back; required only for a partial refund of a sale paid in cash.</summary>
    public decimal? CashRefundAmount { get; set; }
}

public class RefundItemDto
{
    public long VariantId { get; set; }
    public int Quantity { get; set; }
}
