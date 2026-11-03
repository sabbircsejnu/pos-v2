using System.Security.Cryptography;
using System.Net.Http.Headers;
using RetailPOS.Application.AuditEvents.Abstractions;

namespace RetailPOS.Application.ConfidentialFiles;

public sealed class ConfidentialFileService(
    IConfidentialFileAccess access,
    IConfidentialContentInspector inspector,
    IConfidentialFileEncryption encryption,
    IPrivateBlobStore blobs,
    IConfidentialFileVersionStore versions,
    IRequiredAuditTransaction audit,
    ConfidentialFileLimits limits,
    TimeProvider clock)
{
    public async Task<ConfidentialFileVersion> StageAndCommitVersionAsync(
        ConfidentialFileUpload upload, CancellationToken cancellationToken)
    {
        if (upload.BusinessId <= 0 || upload.SubjectId <= 0 || upload.Version <= 0
            || !Enum.IsDefined(upload.Classification))
            throw new ArgumentException("Positive Business, subject and version are required.");
        if (limits.MaxBytes <= 0 || limits.MaxBytes > int.MaxValue)
            throw new InvalidOperationException("Approved confidential upload limits are required.");
        await access.RequireAsync(upload.BusinessId, upload.SubjectId, upload.Classification, "upload", cancellationToken);
        var fileName = upload.FileName;
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255
            || fileName.Any(char.IsControl) || fileName.Contains('/') || fileName.Contains('\\')
            || fileName.Contains(':') || fileName is "." or ".." || fileName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("A safe confidential filename is required.");
        if (upload.UploadedByUserId <= 0)
            throw new UnauthorizedAccessException("An authenticated uploading user is required.");
        if (upload.ExpectedSubjectVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(upload), "A current metadata version is required.");
        if (upload.ExpiresAt.HasValue && upload.ExpiresAt <= clock.GetUtcNow())
            throw new ArgumentException("An upload cannot already be expired.");
        if (upload.ReplacesVersionId is { } previousId)
        {
            var previous = await versions.FindAsync(upload.BusinessId, previousId, cancellationToken)
                ?? throw new KeyNotFoundException("The replaced version does not exist in this Business.");
            if (previous.BusinessId != upload.BusinessId || previous.SubjectId != upload.SubjectId
                || previous.Classification != upload.Classification
                || upload.Version <= previous.Version)
                throw new InvalidOperationException("Replacement must create a later version of the same classified subject.");
        }

        using var buffer = new SensitiveMemoryStream();
        var chunk = new byte[81920];
        try
        {
            while (true)
            {
                var count = await upload.Content.ReadAsync(chunk, cancellationToken);
                if (count == 0) break;
                if (buffer.Length + count > limits.MaxBytes)
                    throw new ArgumentException("Confidential content exceeds the approved limit.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            }
        }
        finally { CryptographicOperations.ZeroMemory(chunk); }
        if (buffer.Length == 0) throw new ArgumentException("Confidential content is empty.");
        var plaintext = buffer.ToArray();
        try
        {
            var validated = await inspector.ValidateAsync(plaintext, fileName, cancellationToken);
            if (string.IsNullOrWhiteSpace(validated.ContentType)
                || !Path.GetExtension(fileName).Equals(validated.Extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Content bytes and filename extension do not agree.");
            if (!MediaTypeHeaderValue.TryParse(upload.DeclaredContentType, out var declaredType)
                || !string.Equals(declaredType.MediaType, validated.ContentType,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Declared MIME type does not match the validated file content.");
            var contentHash = Convert.ToHexString(SHA256.HashData(plaintext));
            ConfidentialFileScanStatus scan;
            try
            {
                scan = await inspector.ScanAsync(plaintext, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await RecordRejectedVersionAsync(upload, fileName, validated.ContentType, plaintext,
                    contentHash, ConfidentialFileScanStatus.Error, exception, cancellationToken);
                throw new ConfidentialFileScanException(
                    ConfidentialFileScanStatus.Error, "Confidential content scanning failed.", exception);
            }
            if (scan != ConfidentialFileScanStatus.Clean)
            {
                await RecordRejectedVersionAsync(upload, fileName, validated.ContentType, plaintext,
                    contentHash, scan, null, cancellationToken);
                throw new ConfidentialFileScanException(
                    scan, $"Confidential scan is not clean ({scan}); publication denied.");
            }
            var encrypted = await encryption.EncryptAsync(plaintext, cancellationToken);
            if (encrypted.Bytes.Length == 0 || string.IsNullOrWhiteSpace(encrypted.KeyReference))
                throw new InvalidOperationException("Confidential encryption did not produce a key-bound payload.");
            var key = await blobs.StageAsync(upload.BusinessId, encrypted.Bytes, cancellationToken);
            var version = new ConfidentialFileVersion(Guid.NewGuid(), upload.BusinessId, upload.SubjectId,
                upload.Version, upload.Classification, key, fileName, validated.ContentType, plaintext.LongLength,
                contentHash, encrypted.KeyReference, scan,
                upload.ExpiresAt, null, upload.ReplacesVersionId,
                upload.RetainUntil, upload.IsLegalHold, upload.UploadedByUserId,
                upload.ExpectedSubjectVersion);
            // Blob staging is deliberately not called atomic with the database; retention policy owns orphan cleanup.
            try
            {
                await audit.ExecuteAsync(Operation(version, "upload"),
                    async token => { await versions.AddAsync(version, token); return true; }, cancellationToken);
            }
            catch (Exception commitFailure)
            {
                try
                {
                    await blobs.DeleteOwnedStageAsync(upload.BusinessId, key, CancellationToken.None);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "Confidential metadata commit failed and its owned staged blob could not be cleaned up.",
                        commitFailure, cleanupFailure);
                }
                throw;
            }
            return version;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private async Task RecordRejectedVersionAsync(
        ConfidentialFileUpload upload,
        string fileName,
        string contentType,
        byte[] plaintext,
        string contentHash,
        ConfidentialFileScanStatus status,
        Exception? scanError,
        CancellationToken cancellationToken)
    {
        var version = new ConfidentialFileVersion(
            Guid.NewGuid(), upload.BusinessId, upload.SubjectId, upload.Version,
            upload.Classification, string.Empty, fileName, contentType, plaintext.LongLength,
            contentHash, string.Empty, status, upload.ExpiresAt, null, upload.ReplacesVersionId,
            upload.RetainUntil, upload.IsLegalHold, upload.UploadedByUserId,
            upload.ExpectedSubjectVersion);
        var summary = scanError is null
            ? $"Confidential upload rejected by content scan ({status})."
            : "Confidential upload content scan returned an error.";
        await audit.ExecuteAsync(
            new RequiredAuditOperation(
                version.BusinessId,
                "upload_rejected",
                "hr",
                summary,
                "EmployeeDocument",
                version.SubjectId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            async token =>
            {
                await versions.AddRejectedAsync(version, token);
                return true;
            },
            cancellationToken);
    }

    public async Task<AuthorizedConfidentialFile> OpenAuthorizedReadAsync(
        long businessId, Guid versionId, CancellationToken cancellationToken)
    {
        if (businessId <= 0) throw new UnauthorizedAccessException("A verified Business is required.");
        var version = await versions.FindAsync(businessId, versionId, cancellationToken)
            ?? throw new KeyNotFoundException("Private version was not found.");
        if (version.BusinessId != businessId)
            throw new UnauthorizedAccessException("Private version Business mismatch.");
        await access.RequireAsync(
            businessId, version.SubjectId, version.Classification, "download", cancellationToken, version.Id);
        if (version.ScanStatus != ConfidentialFileScanStatus.Clean || version.RevokedAt.HasValue
            || (version.ExpiresAt.HasValue && version.ExpiresAt <= clock.GetUtcNow()))
            throw new UnauthorizedAccessException("Private version is unavailable, expired or revoked.");
        if (limits.MaxBytes <= 0 || limits.MaxBytes > int.MaxValue
            || limits.MaxCiphertextBytes <= 0 || limits.MaxCiphertextBytes > int.MaxValue)
            throw new InvalidOperationException("Approved plaintext and ciphertext read limits are required.");
        if (version.SizeBytes <= 0 || version.SizeBytes > limits.MaxBytes || !Enum.IsDefined(version.Classification))
            throw new InvalidDataException("Private version exceeds the approved content limits or classification.");
        await using var stream = await blobs.OpenAsync(businessId, version.StorageKey, cancellationToken);
        using var ciphertext = new MemoryStream();
        var encryptedChunk = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(encryptedChunk, cancellationToken);
            if (count == 0) break;
            if (ciphertext.Length + count > limits.MaxCiphertextBytes)
                throw new InvalidDataException("Private encrypted content exceeds its approved bound.");
            await ciphertext.WriteAsync(encryptedChunk.AsMemory(0, count), cancellationToken);
        }
        var plaintext = await encryption.DecryptAsync(ciphertext.ToArray(), version.EncryptionKeyReference, cancellationToken);
        try
        {
            if (plaintext.LongLength != version.SizeBytes
                || !Convert.ToHexString(SHA256.HashData(plaintext)).Equals(version.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Private content integrity validation failed.");
            await audit.ExecuteAsync(Operation(version, "download"), _ => Task.FromResult(true), cancellationToken);
            return new AuthorizedConfidentialFile(version.FileName, version.ContentType,
                new SensitiveMemoryStream(plaintext));
        }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
    }

    private static RequiredAuditOperation Operation(ConfidentialFileVersion version, string action) =>
        new(version.BusinessId, action,
            version.Classification is ConfidentialFileClassification.EmployeeDocument
                or ConfidentialFileClassification.LeaveRequestAttachment ? "hr" : "payroll",
            "Confidential file access", version.Classification.ToString(), version.Id.ToString(),
            [new AuditEventEntityWriteRequest(version.Classification.ToString(), version.Id.ToString(), action,
                Fields: [new AuditEventFieldWriteRequest("Version", null, version.Version),
                    new AuditEventFieldWriteRequest("OutputHash", null, version.Sha256)])]);

    private sealed class SensitiveMemoryStream : MemoryStream
    {
        private readonly byte[]? _ownedBytes;
        public SensitiveMemoryStream() { }
        public SensitiveMemoryStream(byte[] bytes) : base(bytes, writable: false) => _ownedBytes = bytes;
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_ownedBytes is not null) CryptographicOperations.ZeroMemory(_ownedBytes);
                else if (TryGetBuffer(out var buffer)) CryptographicOperations.ZeroMemory(buffer.AsSpan());
            }
            base.Dispose(disposing);
        }
    }
}
