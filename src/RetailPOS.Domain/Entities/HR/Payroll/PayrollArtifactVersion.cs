namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollArtifactVersion
{
    public Guid Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollRunId { get; set; }
    public int RunRevision { get; set; }
    public long PayrollEmployeeResultId { get; set; }
    public string ArtifactType { get; set; } = "Payslip";
    public int VersionNo { get; set; }
    public string PayrollSnapshotHash { get; set; } = "";
    public string PaymentInfoSnapshotHash { get; set; } = "";
    public string TemplateVersion { get; set; } = "";
    public string Format { get; set; } = "PDF";
    public string StorageKey { get; set; } = "";
    public string EncryptionKeyReference { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string ContentType { get; set; } = "application/pdf";
    public long ByteLength { get; set; }
    public string Classification { get; set; } = "ConfidentialPayroll";
    public string ScanStatus { get; set; } = "Clean";
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RetainUntilUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacesVersionId { get; set; }
    public string FileName { get; set; } = "";
}

public sealed class PayrollPayslipDelivery
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public Guid ArtifactVersionId { get; set; }
    public string RecipientSnapshot { get; set; } = "";
    public long RequestedByUserId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ProviderReference { get; set; }
    public string? FailureCode { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public int Version { get; set; } = 1;
}
