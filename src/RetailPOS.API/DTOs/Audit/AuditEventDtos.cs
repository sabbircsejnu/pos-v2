namespace RetailPOS.API.DTOs.Audit;

public class AuditEventListItemDto
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UserName { get; set; }
    public string? ActingAsRole { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string ActionSummary { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? PrimaryEntityType { get; set; }
    public string? PrimaryEntityId { get; set; }
    public int AffectedEntitiesCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
}

public class AuditEventListResponseDto
{
    public List<AuditEventListItemDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class AuditEventDetailsDto : AuditEventListItemDto
{
    public string? OutletName { get; set; }
    public string? RealUserName { get; set; }
    public string? IpAddress { get; set; }
    public string? RawIp { get; set; }
    public string? LocalMachineIp { get; set; }
    public bool IsLocalRequest { get; set; }
    public string? Device { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }
    public string? RequestMethod { get; set; }
    public string? RequestPath { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ModulesInvolved { get; set; } = new();
    public List<AuditAffectedEntityDto> AffectedEntities { get; set; } = new();
}

public class AuditAffectedEntityDto
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public int FieldsChangedCount { get; set; }
    public bool IsInternalOperation { get; set; }
    public string? InternalOperationName { get; set; }
    public List<AuditFieldChangeDto> FieldChanges { get; set; } = new();
}

public class AuditFieldChangeDto
{
    public string FieldName { get; set; } = string.Empty;
    public string? DisplayLabel { get; set; }
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
    public string? OldDisplayValue { get; set; }
    public string? NewDisplayValue { get; set; }
    public string? ReferenceEntityType { get; set; }
    public bool IsReferenceField { get; set; }
    public bool Redacted { get; set; }
}

public class AuditExportRequestDto : AuditListFilterDto
{
    public string Format { get; set; } = "csv";
}

public class AuditListFilterDto
{
    public string? Search { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public long? UserId { get; set; }
    public List<string>? ActionType { get; set; }
    public List<string>? Module { get; set; }
    public List<string>? Source { get; set; }
    public long? OutletId { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
