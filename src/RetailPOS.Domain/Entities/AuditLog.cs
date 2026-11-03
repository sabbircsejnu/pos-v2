namespace RetailPOS.Core.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public string? Details { get; set; } // JSON as string
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User? User { get; set; }
}
