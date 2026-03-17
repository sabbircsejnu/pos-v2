namespace RetailPOS.API.DTOs.Customer;

public class CustomerDto
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int LoyaltyPoints { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalPurchases { get; set; }
}
