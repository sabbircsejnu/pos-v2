namespace RetailPOS.API.DTOs.Sale;

public class RefundSaleDto
{
    public List<RefundItemDto> Items { get; set; } = new();
    public string? Reason { get; set; }
}

public class RefundItemDto
{
    public long VariantId { get; set; }
    public int Quantity { get; set; }
}
