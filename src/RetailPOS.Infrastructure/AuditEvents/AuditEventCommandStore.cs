using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.AuditEvents;

public sealed class AuditEventCommandStore(IAuditService auditService)
    : IAuditEventCommandStore
{
    public Task RecordAsync(
        AuditEventWriteRequest request,
        CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEventInput
        {
            ActionType = request.ActionType,
            Module = request.Module,
            Summary = request.Summary,
            PrimaryEntity = request.PrimaryEntityType is null
                ? null
                : (request.PrimaryEntityType, request.PrimaryEntityId ?? string.Empty),
            Entities = request.Entities?.Select(entity => new AuditEntityInput(
                entity.EntityType,
                entity.EntityId,
                entity.OperationType,
                fields: entity.Fields?.Select(field =>
                    (field.FieldName, field.OldValue, field.NewValue)).ToArray(),
                metadata: entity.Metadata)).ToArray() ?? [],
            Metadata = request.Metadata
        }, cancellationToken);

    public Task RecordFailureAsync(
        string actionType,
        string module,
        string summary,
        string? entityType,
        string? entityId,
        string errorMessage,
        CancellationToken cancellationToken) =>
        auditService.RecordFailureAsync(
            actionType,
            module,
            summary,
            entityType,
            entityId,
            errorMessage,
            cancellationToken);

    public Task FlushScopeAsync(CancellationToken cancellationToken) =>
        auditService.FlushScopeAsync(cancellationToken);
}
