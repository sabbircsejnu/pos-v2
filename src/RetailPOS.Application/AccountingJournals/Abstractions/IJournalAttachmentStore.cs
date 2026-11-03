using RetailPOS.Application.AccountingJournals.ReadModels;

namespace RetailPOS.Application.AccountingJournals.Abstractions;

public interface IJournalAttachmentStore
{
    Task<IReadOnlyList<JournalAttachmentReadModel>> GetAttachmentsAsync(
        long businessId,
        long journalEntryId,
        CancellationToken cancellationToken);

    Task<JournalAttachmentReadModel> UploadAsync(
        long businessId,
        long journalEntryId,
        Stream content,
        string? fileName,
        string? contentType,
        long fileSizeBytes,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<JournalAttachmentDownloadReadModel> DownloadAsync(
        long businessId,
        long journalEntryId,
        long attachmentId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        long businessId,
        long journalEntryId,
        long attachmentId,
        CancellationToken cancellationToken);
}

public interface IJournalAttachmentFileStore
{
    Task<string> SaveAsync(
        long businessId,
        long journalEntryId,
        string storedFileName,
        Stream content,
        CancellationToken cancellationToken);

    bool Exists(string relativePath);

    Task<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken);

    void DeleteIfExists(string relativePath);
}

public interface IJournalAttachmentTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
