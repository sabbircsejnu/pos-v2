namespace RetailPOS.Core.Entities;

public class Expense
{
    public long Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime ExpenseDate { get; set; }
    public long? OutletId { get; set; }

    // Navigation properties
    public virtual Outlet? Outlet { get; set; }
}
