namespace RetailPOS.Core.Entities.Audit;

public class AuditEventFieldChange
{
    public long Id { get; set; }
    public long AuditEventEntityId { get; set; }

    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>Human-readable snapshot of the old referenced value at audit-write time.</summary>
    public string? OldDisplayValue { get; set; }

    /// <summary>Human-readable snapshot of the new referenced value at audit-write time.</summary>
    public string? NewDisplayValue { get; set; }

    /// <summary>Domain type of the referenced entity (e.g. "Supplier", "User").</summary>
    public string? ReferenceEntityType { get; set; }

    /// <summary>True when the field is a foreign-key reference to another entity.</summary>
    public bool IsReferenceField { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual AuditEventEntity? AuditEventEntity { get; set; }
}
