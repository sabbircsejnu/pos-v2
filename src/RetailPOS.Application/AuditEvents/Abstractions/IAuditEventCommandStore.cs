namespace RetailPOS.Application.AuditEvents.Abstractions;

public interface IAuditEventCommandStore
{
    Task RecordAsync(AuditEventWriteRequest request, CancellationToken cancellationToken);

    Task RecordFailureAsync(
        string actionType,
        string module,
        string summary,
        string? entityType,
        string? entityId,
        string errorMessage,
        CancellationToken cancellationToken);

    Task FlushScopeAsync(CancellationToken cancellationToken);
}

public sealed record AuditEventWriteRequest(
    string ActionType,
    string Module,
    string Summary,
    string? PrimaryEntityType = null,
    string? PrimaryEntityId = null,
    IReadOnlyList<AuditEventEntityWriteRequest>? Entities = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AuditEventEntityWriteRequest(
    string EntityType,
    string EntityId,
    string OperationType,
    string? Metadata = null,
    IReadOnlyList<AuditEventFieldWriteRequest>? Fields = null);

public sealed record AuditEventFieldWriteRequest(string FieldName, object? OldValue, object? NewValue);
