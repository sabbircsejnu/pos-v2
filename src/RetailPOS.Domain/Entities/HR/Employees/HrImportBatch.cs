namespace RetailPOS.Core.Entities.HR.Employees;

public sealed class HrImportBatch
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string ImportType { get; set; } = string.Empty;
    public string TemplateVersion { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string FileStorageKey { get; set; } = string.Empty;
    public string EncryptionKeyReference { get; set; } = string.Empty;
    public string Status { get; set; } = "Uploaded";
    public bool IsDryRun { get; set; } = true;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int Revision { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public long? ExecutedBy { get; set; }
    public DateTime? ExecutedAtUtc { get; set; }
    public DateTime RetainUntilUtc { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class HrImportRow
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ImportBatchId { get; set; }
    public int Revision { get; set; }
    public int RowNo { get; set; }
    public string ExternalKey { get; set; } = string.Empty;
    public string RawJson { get; set; } = "{}";
    public string ValidationStatus { get; set; } = string.Empty;
    public string ErrorJson { get; set; } = "[]";
    public string? CreatedEntityType { get; set; }
    public long? CreatedEntityId { get; set; }
}
