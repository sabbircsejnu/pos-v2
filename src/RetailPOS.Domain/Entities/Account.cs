namespace RetailPOS.Core.Entities;

public class Account
{
    public long Id { get; set; }
    public long? BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // asset, liability, expense, revenue
    public decimal Balance { get; set; } = 0;

    // Navigation properties
    public virtual Business? Business { get; set; }
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
