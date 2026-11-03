namespace RetailPOS.API.DTOs.Audit;

public sealed class PosDiscountAuditDto
{
    public string Action { get; set; } = string.Empty;
    public string? FromType { get; set; }
    public string? ToType { get; set; }
    public long? OutletId { get; set; }
    public long? TerminalId { get; set; }
    public Dictionary<string, object?>? Payload { get; set; }
}
