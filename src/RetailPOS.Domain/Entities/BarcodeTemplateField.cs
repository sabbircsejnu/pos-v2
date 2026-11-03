namespace RetailPOS.Core.Entities;

public class BarcodeTemplateField
{
    public long Id { get; set; }
    public long TemplateId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public decimal? X { get; set; }
    public decimal? Y { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? FontSize { get; set; }
    public string? FontWeight { get; set; }
    public string? Align { get; set; }

    // Navigation properties
    public virtual BarcodeTemplate Template { get; set; } = null!;
}
