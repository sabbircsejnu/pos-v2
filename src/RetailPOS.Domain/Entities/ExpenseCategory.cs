namespace RetailPOS.Core.Entities;

public class ExpenseCategory
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long ExpenseAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business Business { get; set; } = null!;
    public virtual ChartAccount ExpenseAccount { get; set; } = null!;
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}