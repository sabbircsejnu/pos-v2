namespace RetailPOS.Core.Entities;

public class BusinessFeatureSetting
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int? LimitValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business Business { get; set; } = null!;
}
