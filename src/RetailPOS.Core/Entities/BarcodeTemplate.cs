namespace RetailPOS.Core.Entities;

public class BarcodeTemplate
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = "retail";
    public string PaperType { get; set; } = "label";
    public decimal LabelWidthMm { get; set; } = 60;
    public decimal LabelHeightMm { get; set; } = 40;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public long? CreatedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual ICollection<BarcodeTemplateField> Fields { get; set; } = new List<BarcodeTemplateField>();
}
