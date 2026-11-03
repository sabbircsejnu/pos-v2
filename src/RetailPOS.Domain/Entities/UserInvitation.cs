namespace RetailPOS.Core.Entities;

public class UserInvitation
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Purpose { get; set; } = "onboarding";
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User User { get; set; } = null!;
}