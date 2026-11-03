namespace RetailPOS.Core.Entities;

public class JournalEntryLine
{
    public long Id { get; set; }
    public long JournalEntryId { get; set; }
    public long AccountId { get; set; }
    public string? Description { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? SupplierId { get; set; }
    public long? CustomerId { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? CostCenterId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }

    public virtual JournalEntry JournalEntry { get; set; } = null!;
    public virtual ChartAccount Account { get; set; } = null!;
    public virtual Outlet? Outlet { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
    public virtual Supplier? Supplier { get; set; }
    public virtual Customer? Customer { get; set; }
}
