namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrEmployeeDocumentVersion
{
    public Guid Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeDocumentId { get; set; }
    public int VersionNo { get; set; }
    public string? StorageKey { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string ScanStatus { get; set; } = "Pending";
    public string EncryptionKeyReference { get; set; } = string.Empty;
    public long UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public Guid? ReplacesVersionId { get; set; }
    public DateTime? RetainUntilUtc { get; set; }
    public bool IsLegalHold { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
