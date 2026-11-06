namespace RetailPOS.Core.Entities;

/// <summary>A supporting document of an expense; its file lives in private storage, never under wwwroot.</summary>
public class ExpenseAttachment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ExpenseId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }

    public virtual Expense Expense { get; set; } = null!;
}
