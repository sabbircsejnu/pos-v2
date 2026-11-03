namespace RetailPOS.Core.Entities.Audit;

public class AuditEventEntity
{
    public long Id { get; set; }
    public long AuditEventId { get; set; }

    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string OperationType { get; set; } = AuditOperationType.Update;
    public int FieldsChangedCount { get; set; }
    public string? Metadata { get; set; }

    /// <summary>
    /// True when this entity change was a side-effect captured automatically by the EF interceptor
    /// while an explicit business-level audit scope was active (e.g., a status update triggered
    /// by "Submit Purchase Order").
    /// </summary>
    public bool IsInternalOperation { get; set; }

    /// <summary>Human-readable label for the internal operation, e.g. "Status Update".</summary>
    public string? InternalOperationName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual AuditEvent? AuditEvent { get; set; }
    public virtual ICollection<AuditEventFieldChange> FieldChanges { get; set; } = new List<AuditEventFieldChange>();
}
