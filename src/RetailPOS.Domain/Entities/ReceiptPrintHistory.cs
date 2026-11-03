namespace RetailPOS.Core.Entities;

public class ReceiptPrintHistory
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long? TerminalId { get; set; }
    public long? PrintedByUserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public DateTime PrintedAt { get; set; } = DateTime.UtcNow;

    public virtual Sale Sale { get; set; } = null!;
    public virtual PosTerminal? Terminal { get; set; }
    public virtual User? PrintedByUser { get; set; }
}
