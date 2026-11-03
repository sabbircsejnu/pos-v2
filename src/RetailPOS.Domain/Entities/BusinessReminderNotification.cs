namespace RetailPOS.Core.Entities;

public class BusinessReminderNotification
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string ReminderType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime TargetAt { get; set; }
    public bool IsDelivered { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business Business { get; set; } = null!;
}
