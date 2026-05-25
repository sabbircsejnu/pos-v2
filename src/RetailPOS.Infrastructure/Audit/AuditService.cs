using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Audit;

public interface IAuditService
{
    Task RecordAsync(AuditEventInput input, CancellationToken ct = default);
    Task RecordFailureAsync(string actionType, string module, string summary,
        string? entityType, string? entityId, string errorMessage, CancellationToken ct = default);

    AuditEvent BuildPendingEvent(string actionType, string module, string summary,
        string? primaryEntityType, string? primaryEntityId);

    void AttachEntity(AuditEvent header, string entityType, string entityId, string operationType,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)>? fields = null,
        string? metadata = null);

    /// <summary>
    /// Flushes the active audit scope (if any) by writing a single <see cref="AuditEvent"/> that
    /// contains all entity changes accumulated while the scope was open, then ends the scope.
    /// No-op when no scope is active.
    /// </summary>
    Task FlushScopeAsync(CancellationToken ct = default);
}

public sealed class AuditEventInput
{
    public string ActionType { get; init; } = AuditActionType.Update;
    public string Module { get; init; } = AuditModule.System;
    public string Summary { get; init; } = string.Empty;
    public (string Type, string Id)? PrimaryEntity { get; init; }
    public IReadOnlyList<AuditEntityInput> Entities { get; init; } = Array.Empty<AuditEntityInput>();
    public string Status { get; init; } = AuditStatus.Success;
    public string? ErrorMessage { get; init; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}

public sealed class AuditEntityInput
{
    public string EntityType { get; }
    public string EntityId { get; }
    public string OperationType { get; }
    public IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)> Fields { get; }
    public string? Metadata { get; }

    public AuditEntityInput(string entityType, string entityId, string operationType,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)>? fields = null,
        string? metadata = null)
    {
        EntityType = entityType;
        EntityId = entityId;
        OperationType = operationType;
        Fields = fields ?? Array.Empty<(string, object?, object?)>();
        Metadata = metadata;
    }
}

public sealed class AuditService : IAuditService
{
    private readonly RetailPOSDbContext _db;
    private readonly IAuditContext _ctx;
    private readonly IServiceProvider _sp;
    private readonly ILogger<AuditService> _logger;

    public AuditService(RetailPOSDbContext db, IAuditContext ctx, IServiceProvider sp,
        ILogger<AuditService> logger)
    {
        _db = db;
        _ctx = ctx;
        _sp = sp;
        _logger = logger;
    }

    public async Task RecordAsync(AuditEventInput input, CancellationToken ct = default)
    {
        var header = BuildPendingEvent(input.ActionType, input.Module, input.Summary,
            input.PrimaryEntity?.Type, input.PrimaryEntity?.Id);
        header.Status = input.Status;
        header.ErrorMessage = input.ErrorMessage;
        if (input.Metadata is { Count: > 0 })
        {
            header.Metadata = JsonSerializer.Serialize(input.Metadata);
        }

        foreach (var e in input.Entities)
        {
            AttachEntity(header, e.EntityType, e.EntityId, e.OperationType, e.Fields, e.Metadata);
        }

        _db.AuditEvents.Add(header);
        await _db.SaveChangesAsync(ct);
    }

    public AuditEvent BuildPendingEvent(string actionType, string module, string summary,
        string? primaryEntityType, string? primaryEntityId)
    {
        return new AuditEvent
        {
            CorrelationId = _ctx.IsInitialized ? _ctx.CorrelationId : Guid.NewGuid(),
            BusinessId = _ctx.BusinessId,
            OutletId = _ctx.OutletId,
            RealUserId = _ctx.RealUserId,
            ActingUserId = _ctx.ActingUserId,
            RealRoleId = _ctx.RealRoleId,
            ActingRoleId = _ctx.ActingRoleId,
            ActionType = actionType,
            ActionSummary = TruncateRequired(summary, 500),
            Module = module,
            Source = _ctx.IsInitialized ? _ctx.Source : AuditSource.SystemJob,
            PrimaryEntityType = primaryEntityType,
            PrimaryEntityId = primaryEntityId,
            RequestMethod = _ctx.RequestMethod,
            RequestPath = _ctx.RequestPath,
            IpAddress = Truncate(_ctx.IpAddress, 45),
            RawIp = Truncate(_ctx.RawIp, 45),
            LocalMachineIp = Truncate(_ctx.LocalMachineIp, 45),
            IsLocalRequest = _ctx.IsLocalRequest,
            UserAgent = Truncate(_ctx.UserAgent, 500),
            DeviceName = _ctx.DeviceName,
            Browser = _ctx.Browser,
            Os = _ctx.Os,
            Status = AuditStatus.Success,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public void AttachEntity(AuditEvent header, string entityType, string entityId, string operationType,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)>? fields = null,
        string? metadata = null)
    {
        var entityRow = new AuditEventEntity
        {
            EntityType = entityType,
            EntityId = entityId,
            OperationType = operationType,
            Metadata = metadata,
            CreatedAt = DateTime.UtcNow,
        };

        if (fields is { Count: > 0 })
        {
            foreach (var f in fields)
            {
                var sensitive = AuditFieldRedaction.IsSensitive(entityType, f.FieldName);
                entityRow.FieldChanges.Add(new AuditEventFieldChange
                {
                    FieldName = f.FieldName,
                    OldValue = sensitive ? JsonSerializer.Serialize(AuditFieldRedaction.RedactedToken) : ToJson(f.OldValue),
                    NewValue = sensitive ? JsonSerializer.Serialize(AuditFieldRedaction.RedactedToken) : ToJson(f.NewValue),
                    CreatedAt = DateTime.UtcNow,
                });
            }
            entityRow.FieldsChangedCount = fields.Count;
        }

        header.Entities.Add(entityRow);
    }

    public async Task FlushScopeAsync(CancellationToken ct = default)
    {
        if (!_ctx.HasActiveScope) return;

        var entities = _ctx.DrainScopeEntities();
        var failed = _ctx.ScopeFailed;
        var errorMessage = _ctx.ScopeErrorMessage;

        var header = new AuditEvent
        {
            CorrelationId = _ctx.IsInitialized ? _ctx.CorrelationId : Guid.NewGuid(),
            BusinessId = _ctx.BusinessId,
            OutletId = _ctx.OutletId,
            RealUserId = _ctx.RealUserId,
            ActingUserId = _ctx.ActingUserId,
            RealRoleId = _ctx.RealRoleId,
            ActingRoleId = _ctx.ActingRoleId,
            ActionType = TruncateRequired(_ctx.ScopeActionType, 50),
            ActionSummary = TruncateRequired(_ctx.ScopeActionSummary, 500),
            Module = TruncateRequired(_ctx.ScopeModule, 30),
            Source = _ctx.IsInitialized ? _ctx.Source : AuditSource.SystemJob,
            PrimaryEntityType = _ctx.ScopePrimaryEntityType,
            PrimaryEntityId = _ctx.ScopePrimaryEntityId,
            RequestMethod = _ctx.RequestMethod,
            RequestPath = _ctx.RequestPath,
            IpAddress = Truncate(_ctx.IpAddress, 45),
            RawIp = Truncate(_ctx.RawIp, 45),
            LocalMachineIp = Truncate(_ctx.LocalMachineIp, 45),
            IsLocalRequest = _ctx.IsLocalRequest,
            UserAgent = Truncate(_ctx.UserAgent, 500),
            DeviceName = _ctx.DeviceName,
            Browser = _ctx.Browser,
            Os = _ctx.Os,
            Status = failed ? AuditStatus.Failed : AuditStatus.Success,
            ErrorMessage = failed ? Truncate(errorMessage, 4000) : null,
            CreatedAt = DateTime.UtcNow,
        };

        // Resolve reference display names for all accumulated field changes
        var resolver = new AuditReferenceSnapshotResolver();
        try
        {
            var allFields = entities.SelectMany(e => e.Fields.Select(f => (f.Name, f.Old, f.New)));
            await resolver.PreloadAsync(_db, allFields, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audit scope snapshot pre-load failed — scope will be saved with raw values only. " +
                "ActionSummary={ActionSummary}", _ctx.ScopeActionSummary);
        }

        foreach (var e in entities)
        {
            var entityRow = new AuditEventEntity
            {
                EntityType = e.EntityType,
                EntityId = e.EntityId,
                OperationType = e.OperationType,
                IsInternalOperation = e.IsInternalOperation,
                InternalOperationName = e.InternalOperationName,
                CreatedAt = DateTime.UtcNow,
            };

            foreach (var f in e.Fields)
            {
                var sensitive = AuditFieldRedaction.IsSensitive(e.EntityType, f.Name);
                AuditReferenceSnapshotResolver.ResolvedSnapshot? snapshot = null;
                if (!sensitive)
                {
                    try { snapshot = resolver.Resolve(f.Name, f.Old, f.New); }
                    catch (Exception fieldEx)
                    {
                        _logger.LogWarning(fieldEx,
                            "Audit scope snapshot resolve failed for field {FieldName} on {EntityType}",
                            f.Name, e.EntityType);
                    }
                }
                entityRow.FieldChanges.Add(new AuditEventFieldChange
                {
                    FieldName = f.Name,
                    OldValue = sensitive
                        ? JsonSerializer.Serialize(AuditFieldRedaction.RedactedToken)
                        : ToJson(f.Old),
                    NewValue = sensitive
                        ? JsonSerializer.Serialize(AuditFieldRedaction.RedactedToken)
                        : ToJson(f.New),
                    OldDisplayValue = snapshot?.OldDisplayValue,
                    NewDisplayValue = snapshot?.NewDisplayValue,
                    ReferenceEntityType = snapshot?.ReferenceEntityType,
                    IsReferenceField = snapshot is not null,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            entityRow.FieldsChangedCount = entityRow.FieldChanges.Count;
            header.Entities.Add(entityRow);
        }

        _db.AuditEvents.Add(header);
        await _db.SaveChangesAsync(ct);

        // Clear scope state after successful flush
        _ctx.EndScope();
    }

    public async Task RecordFailureAsync(string actionType, string module, string summary,
        string? entityType, string? entityId, string errorMessage, CancellationToken ct = default)
    {
        var scopeFactory = _sp.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var freshDb = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var header = BuildPendingEvent(actionType, module, summary, entityType, entityId);
        header.Status = AuditStatus.Failed;
        header.ErrorMessage = Truncate(errorMessage, 4000); // nullable — null stays null
        freshDb.AuditEvents.Add(header);
        await freshDb.SaveChangesAsync(ct);
    }

    private static string? ToJson(object? v)
    {
        if (v is null) return null;
        try { return JsonSerializer.Serialize(v); }
        catch { return JsonSerializer.Serialize(v.ToString()); }
    }

    // Returns null for null inputs (used for nullable DB columns like UserAgent, IpAddress).
    private static string? Truncate(string? s, int max)
    {
        if (s is null) return null;
        return s.Length <= max ? s : s[..max];
    }

    // Returns empty string for null inputs (used for required non-null fields like ActionSummary).
    private static string TruncateRequired(string? s, int max)
    {
        if (s is null) return string.Empty;
        return s.Length <= max ? s : s[..max];
    }
}
