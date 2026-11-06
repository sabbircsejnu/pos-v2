namespace RetailPOS.Application.Expenses.Abstractions;

public sealed class ExpenseAttachmentOptions
{
    public const string SectionName = "ExpenseAttachments";

    /// <summary>Private folder outside wwwroot. Relative paths resolve from the API content root.</summary>
    public string StorageRoot { get; set; } = Path.Combine("App_Data", "expense-attachments");

    /// <summary>Kestrel's 30 MB request limit also applies to a single upload.</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public int MaxFilesPerExpense { get; set; } = 10;
}

public sealed record ExpenseAttachmentReadModel(
    long Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTime CreatedAt,
    long? CreatedBy);

public sealed record ExpenseAttachmentContentReadModel(
    byte[] Content,
    string FileName,
    string ContentType);

public interface IExpenseAttachmentStore
{
    Task<IReadOnlyList<ExpenseAttachmentReadModel>> ListAsync(
        long businessId,
        long expenseId,
        CancellationToken cancellationToken);

    Task<ExpenseAttachmentReadModel> UploadAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        Stream content,
        string? fileName,
        CancellationToken cancellationToken);

    Task<ExpenseAttachmentContentReadModel> GetContentAsync(
        long businessId,
        long expenseId,
        long attachmentId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        long attachmentId,
        CancellationToken cancellationToken);

    /// <summary>Storage keys to remove once the owning expense is gone. Exposes nothing to the caller's client.</summary>
    Task<IReadOnlyList<string>> GetStorageKeysAsync(
        long businessId,
        long expenseId,
        CancellationToken cancellationToken);

    /// <summary>Best-effort removal after commit; a failure is logged and never fails the committed operation.</summary>
    void RemoveFiles(IReadOnlyList<string> storageKeys);
}

public interface IExpenseAttachmentFileStore
{
    Task<string> SaveAsync(
        long businessId,
        long expenseId,
        string extension,
        Stream content,
        CancellationToken cancellationToken);

    bool Exists(string storageKey);

    Task<byte[]> ReadAllBytesAsync(string storageKey, CancellationToken cancellationToken);

    void DeleteIfExists(string storageKey);
}
