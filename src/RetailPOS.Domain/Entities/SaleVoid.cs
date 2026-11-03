namespace RetailPOS.Core.Entities;

public class SaleVoid
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long? VoidedByUserId { get; set; }
    public string PreviousStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime VoidedAt { get; set; } = DateTime.UtcNow;

    public virtual Sale Sale { get; set; } = null!;
    public virtual User? VoidedByUser { get; set; }
}
