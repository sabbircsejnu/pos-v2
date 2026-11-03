namespace RetailPOS.Application.AccountingJournals.ReadModels;

public sealed record JournalAttachmentReadModel(
    long Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTime CreatedAt,
    long? CreatedBy);

public sealed record JournalAttachmentDownloadReadModel(
    byte[] Content,
    string FileName,
    string ContentType);
