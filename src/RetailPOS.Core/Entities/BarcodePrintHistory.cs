namespace RetailPOS.Core.Entities;

public class BarcodePrintHistory
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long? OutletId { get; set; }
    public long? TemplateId { get; set; }
    public long? PrintedByUserId { get; set; }
    public string PrintMode { get; set; } = "pdf";
    public decimal LabelWidthMm { get; set; } = 60;
    public decimal LabelHeightMm { get; set; } = 40;
    public int TotalLabels { get; set; }
    public string? SourceModule { get; set; }
    public string? SourceReferenceType { get; set; }
    public long? SourceReferenceId { get; set; }
    public DateTime PrintedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual Outlet? Outlet { get; set; }
    public virtual BarcodeTemplate? Template { get; set; }
    public virtual User? PrintedByUser { get; set; }
    public virtual ICollection<BarcodePrintHistoryItem> Items { get; set; } = new List<BarcodePrintHistoryItem>();
}
