namespace RetailPOS.Application.AuditEvents.ReadModels;

public sealed record AuditEventListItemReadModel(
    long Id,
    DateTime CreatedAt,
    string? UserName,
    string? ActingAsRole,
    string ActionType,
    string ActionSummary,
    string Module,
    string Source,
    string? PrimaryEntityType,
    string? PrimaryEntityId,
    int AffectedEntitiesCount,
    string Status,
    Guid CorrelationId);

public sealed record AuditEventListReadModel(
    IReadOnlyList<AuditEventListItemReadModel> Items,
    int Total);

public sealed record AuditEventExportEntityReadModel(
    string EntityType,
    string EntityId,
    string OperationType,
    int FieldsChangedCount);

public sealed record AuditEventExportItemReadModel(
    long Id,
    DateTime CreatedAt,
    Guid CorrelationId,
    string Module,
    string Source,
    string ActionType,
    string ActionSummary,
    string? PrimaryEntityType,
    string? PrimaryEntityId,
    long? RealUserId,
    string? IpAddress,
    string? RequestMethod,
    string? RequestPath,
    string Status,
    string? ErrorMessage,
    IReadOnlyList<AuditEventExportEntityReadModel> Entities);

public sealed record AuditEventExportReadModel(
    int Total,
    IReadOnlyList<AuditEventExportItemReadModel> Items,
    bool ConfidentialAccessAudited = false);

public sealed record AuditEventFieldChangeReadModel(
    string FieldName,
    string? OldValue,
    string? NewValue,
    string? OldDisplayValue,
    string? NewDisplayValue,
    string? ReferenceEntityType,
    bool IsReferenceField);

public sealed record AuditAffectedEntityReadModel(
    long Id,
    string EntityType,
    string EntityId,
    string OperationType,
    int FieldsChangedCount,
    bool IsInternalOperation,
    string? InternalOperationName,
    IReadOnlyList<AuditEventFieldChangeReadModel> FieldChanges);

public sealed record AuditEventDetailsReadModel(
    AuditEventListItemReadModel ListItem,
    string? OutletName,
    string? IpAddress,
    string? RawIp,
    string? LocalMachineIp,
    bool IsLocalRequest,
    string? DeviceName,
    string? Browser,
    string? Os,
    string? RequestMethod,
    string? RequestPath,
    string? ErrorMessage,
    IReadOnlyList<AuditAffectedEntityReadModel> AffectedEntities);
