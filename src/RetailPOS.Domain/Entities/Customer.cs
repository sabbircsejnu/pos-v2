namespace RetailPOS.Core.Entities;

public class Customer
{
    public const string WalkInCustomerCode = "WALKIN";
    public const string WalkInCustomerName = "Walk-in Customer";

    public long Id { get; set; }

    /// <summary>Owning Business. Every customer, including the protected Walk-in, belongs to exactly one Business.</summary>
    public long BusinessId { get; set; }
    public string? Name { get; set; }
    public string? CustomerCode { get; set; }
    public bool IsSystem { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public string? Phone { get; set; }

    /// <summary>Canonical phone (see <c>CustomerPhoneNormalizer</c>); unique per Business when present.</summary>
    public string? PhoneNormalized { get; set; }
    public string? Email { get; set; }
    public int LoyaltyPoints { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
