namespace RetailPOS.Application.ConfidentialFiles;

public enum ConfidentialFileClassification { EmployeeDocument, Payslip, PayrollExport, ImportEvidence, LeaveRequestAttachment }
public enum ConfidentialFileScanStatus { Pending, Clean, Rejected, Error }

public sealed record ConfidentialFileVersion(
    Guid Id, long BusinessId, long SubjectId, int Version,
    ConfidentialFileClassification Classification, string StorageKey,
    string FileName, string ContentType, long SizeBytes, string Sha256,
    string EncryptionKeyReference, ConfidentialFileScanStatus ScanStatus,
    DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, Guid? ReplacesVersionId,
    DateTimeOffset? RetainUntil = null, bool IsLegalHold = false, long UploadedByUserId = 0,
    long ExpectedSubjectVersion = 0);

public sealed class ConfidentialFileScanException(
    ConfidentialFileScanStatus status,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public ConfidentialFileScanStatus Status { get; } = status;
}

public sealed record ConfidentialFileUpload(
    long BusinessId, long SubjectId, int Version, ConfidentialFileClassification Classification,
    string FileName, Stream Content, DateTimeOffset? ExpiresAt, Guid? ReplacesVersionId,
    string? DeclaredContentType = null, long UploadedByUserId = 0,
    DateTimeOffset? RetainUntil = null, bool IsLegalHold = false,
    long ExpectedSubjectVersion = 0);

public sealed record ConfidentialContentValidation(string ContentType, string Extension);

public interface IConfidentialFileAccess
{
    Task RequireAsync(long businessId, long subjectId, ConfidentialFileClassification classification,
        string action, CancellationToken cancellationToken, Guid? versionId = null);
}

public interface IConfidentialContentInspector
{
    Task<ConfidentialContentValidation> ValidateAsync(ReadOnlyMemory<byte> content,
        string fileName, CancellationToken cancellationToken);
    Task<ConfidentialFileScanStatus> ScanAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

public sealed record EncryptedConfidentialContent(byte[] Bytes, string KeyReference);

public interface IConfidentialFileEncryption
{
    Task<EncryptedConfidentialContent> EncryptAsync(ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken);
    Task<byte[]> DecryptAsync(ReadOnlyMemory<byte> ciphertext, string keyReference, CancellationToken cancellationToken);
}

public interface IPrivateBlobStore
{
    Task<string> StageAsync(long businessId, ReadOnlyMemory<byte> ciphertext, CancellationToken cancellationToken);
    Task<Stream> OpenAsync(long businessId, string key, CancellationToken cancellationToken);
    Task DeleteOwnedStageAsync(long businessId, string key, CancellationToken cancellationToken);
}

public interface IConfidentialFileVersionStore
{
    // Implementations persist immutable version metadata in the caller's existing-context transaction.
    Task AddAsync(ConfidentialFileVersion version, CancellationToken cancellationToken);
    Task AddRejectedAsync(ConfidentialFileVersion version, CancellationToken cancellationToken);
    Task<ConfidentialFileVersion?> FindAsync(long businessId, Guid versionId, CancellationToken cancellationToken);
}

public sealed record AuthorizedConfidentialFile(string FileName, string ContentType, Stream Content);

public sealed record ConfidentialFileLimits(long MaxBytes, long MaxCiphertextBytes = 0);
