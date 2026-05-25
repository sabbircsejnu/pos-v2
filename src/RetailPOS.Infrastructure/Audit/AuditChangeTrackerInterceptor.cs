using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Audit;

public sealed class AuditChangeTrackerInterceptor : SaveChangesInterceptor
{
    private readonly IAuditContext _ctx;
    private readonly ILogger<AuditChangeTrackerInterceptor> _logger;
    private readonly List<PendingEntity> _pending = new();
    private readonly List<EntityEntry> _pendingEntries = new();

    public AuditChangeTrackerInterceptor(IAuditContext ctx, ILogger<AuditChangeTrackerInterceptor> logger)
    {
        _ctx = ctx;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is RetailPOSDbContext db && _ctx.IsInitialized)
            CaptureSnapshot(db);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is RetailPOSDbContext db && _ctx.IsInitialized)
            CaptureSnapshot(db);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is RetailPOSDbContext db && _ctx.IsInitialized && _pending.Count > 0)
            FlushAudit(db);
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is RetailPOSDbContext db && _ctx.IsInitialized && _pending.Count > 0)
            await FlushAuditAsync(db, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private sealed record PendingField(string Name, object? Old, object? New);
    private sealed class PendingEntity
    {
        public string EntityType = "";
        public string OperationType = "";
        public List<PendingField> Fields = new();
    }

    private void CaptureSnapshot(RetailPOSDbContext db)
    {
        _pending.Clear();
        _pendingEntries.Clear();

        var entries = db.ChangeTracker.Entries()
            .Where(e => !IsAuditEntity(e.Entity))
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var et = entry.Entity.GetType().Name;
            var op = entry.State switch
            {
                EntityState.Added => AuditOperationType.Insert,
                EntityState.Deleted => AuditOperationType.Delete,
                _ => AuditOperationType.Update,
            };

            var pending = new PendingEntity { EntityType = et, OperationType = op };
            foreach (var prop in entry.Properties)
            {
                if (entry.State == EntityState.Modified && !prop.IsModified) continue;
                if (entry.State == EntityState.Modified && Equals(prop.OriginalValue, prop.CurrentValue)) continue;

                var changeOld = entry.State == EntityState.Added ? null : prop.OriginalValue;
                var changeNew = entry.State == EntityState.Deleted ? null : prop.CurrentValue;
                pending.Fields.Add(new PendingField(prop.Metadata.Name, changeOld, changeNew));
            }

            _pending.Add(pending);
            _pendingEntries.Add(entry);
        }
    }

    private void FlushAudit(RetailPOSDbContext db)
    {
        // Snapshot to locals immediately so we can clear the shared lists
        var pending = _pending.ToList();
        var entries = _pendingEntries.ToList();
        _pending.Clear();
        _pendingEntries.Clear();

        try
        {
            // Refresh IDs from saved entries (DB-generated keys are now populated)
            for (int i = 0; i < entries.Count; i++)
            {
                var realId = TryGetEntityId(entries[i]);
                if (realId is null) continue;
                foreach (var f in pending[i].Fields)
                {
                    if (string.Equals(f.Name, "Id", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = pending[i].Fields.IndexOf(f);
                        pending[i].Fields[idx] = f with { New = realId };
                        break;
                    }
                }
            }

            // If a business-level audit scope is active, accumulate entities into it
            // instead of creating a standalone generic AuditEvent.
            if (_ctx.HasActiveScope)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    _ctx.AddEntityToScope(new PendingScopeEntity(
                        pending[i].EntityType,
                        TryGetEntityId(entries[i]) ?? string.Empty,
                        pending[i].OperationType,
                        pending[i].Fields.Select(f => (f.Name, f.Old, f.New)).ToList(),
                        IsInternalOperation: true,
                        InternalOperationName: null));
                }
                return;
            }

            // Pre-load reference display names — fault-tolerant: failure here must not block audit save
            var resolver = new AuditReferenceSnapshotResolver();
            try
            {
                var allFields = pending.SelectMany(p => p.Fields.Select(f => (f.Name, f.Old, f.New)));
                resolver.PreloadAsync(db, allFields, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception snapshotEx)
            {
                _logger.LogWarning(snapshotEx,
                    "Audit snapshot pre-load failed — audit will be saved with raw values only. " +
                    "Entity={EntityType} Action={ActionType}",
                    pending.FirstOrDefault()?.EntityType, pending.FirstOrDefault()?.OperationType);
                // resolver cache is empty — Resolve() will return null for all fields (raw values saved)
            }

            var primary = entries.FirstOrDefault();
            var primaryType = primary?.Entity.GetType().Name;
            var primaryId = primary is null ? null : TryGetEntityId(primary);

            var actionType = pending switch
            {
                _ when pending.All(p => p.OperationType == AuditOperationType.Insert) => AuditActionType.Create,
                _ when pending.All(p => p.OperationType == AuditOperationType.Delete) => AuditActionType.Delete,
                _ => AuditActionType.Update,
            };

            var header = new AuditEvent
            {
                CorrelationId = _ctx.CorrelationId,
                BusinessId = _ctx.BusinessId,
                OutletId = _ctx.OutletId,
                RealUserId = _ctx.RealUserId,
                ActingUserId = _ctx.ActingUserId,
                RealRoleId = _ctx.RealRoleId,
                ActingRoleId = _ctx.ActingRoleId,
                ActionType = actionType,
                ActionSummary = BuildSummary(actionType, primaryType ?? "Unknown", pending.Count),
                Module = AuditModule.System,
                Source = _ctx.Source,
                PrimaryEntityType = primaryType,
                PrimaryEntityId = primaryId,
                RequestMethod = _ctx.RequestMethod,
                RequestPath = _ctx.RequestPath,
                IpAddress = _ctx.IpAddress,
                RawIp = _ctx.RawIp,
                LocalMachineIp = _ctx.LocalMachineIp,
                IsLocalRequest = _ctx.IsLocalRequest,
                UserAgent = Truncate(_ctx.UserAgent, 500),
                DeviceName = _ctx.DeviceName,
                Browser = _ctx.Browser,
                Os = _ctx.Os,
                Status = AuditStatus.Success,
                CreatedAt = DateTime.UtcNow,
            };

            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                var entry = entries[i];
                var entityRow = new AuditEventEntity
                {
                    EntityType = p.EntityType,
                    EntityId = TryGetEntityId(entry) ?? string.Empty,
                    OperationType = p.OperationType,
                    CreatedAt = DateTime.UtcNow,
                };

                foreach (var f in p.Fields)
                {
                    var sensitive = AuditFieldRedaction.IsSensitive(p.EntityType, f.Name);
                    AuditReferenceSnapshotResolver.ResolvedSnapshot? snapshot = null;
                    if (!sensitive)
                    {
                        try { snapshot = resolver.Resolve(f.Name, f.Old, f.New); }
                        catch (Exception fieldEx)
                        {
                            _logger.LogWarning(fieldEx,
                                "Audit snapshot resolve failed for field {FieldName} on {EntityType}",
                                f.Name, p.EntityType);
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

            db.AuditEvents.Add(header);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Audit save failed — business action was committed but audit record was not persisted. " +
                "Entity={EntityType} Action={ActionType}",
                pending.FirstOrDefault()?.EntityType, pending.FirstOrDefault()?.OperationType);
        }
    }

    private async Task FlushAuditAsync(RetailPOSDbContext db, CancellationToken ct)
    {
        // Snapshot state into locals so the finally block always clears the shared lists
        var pending = _pending.ToList();
        var entries = _pendingEntries.ToList();
        _pending.Clear();
        _pendingEntries.Clear();
        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var realId = TryGetEntityId(entries[i]);
                if (realId is null) continue;
                foreach (var f in pending[i].Fields)
                {
                    if (string.Equals(f.Name, "Id", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = pending[i].Fields.IndexOf(f);
                        pending[i].Fields[idx] = f with { New = realId };
                        break;
                    }
                }
            }

            // If a business-level audit scope is active, accumulate entities into it
            // instead of creating a standalone generic AuditEvent.
            if (_ctx.HasActiveScope)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    _ctx.AddEntityToScope(new PendingScopeEntity(
                        pending[i].EntityType,
                        TryGetEntityId(entries[i]) ?? string.Empty,
                        pending[i].OperationType,
                        pending[i].Fields.Select(f => (f.Name, f.Old, f.New)).ToList(),
                        IsInternalOperation: true,
                        InternalOperationName: null));
                }
                return;
            }

            // Pre-load reference display names — fault-tolerant: failure here must not block audit save
            var resolver = new AuditReferenceSnapshotResolver();
            try
            {
                var allFields = pending.SelectMany(p => p.Fields.Select(f => (f.Name, f.Old, f.New)));
                await resolver.PreloadAsync(db, allFields, ct);
            }
            catch (Exception snapshotEx)
            {
                _logger.LogWarning(snapshotEx,
                    "Audit snapshot pre-load failed — audit will be saved with raw values only. " +
                    "Entity={EntityType} Action={ActionType}",
                    pending.FirstOrDefault()?.EntityType, pending.FirstOrDefault()?.OperationType);
                // resolver cache is empty — Resolve() will return null for all fields (raw values saved)
            }

            var primary = entries.FirstOrDefault();
            var primaryType = primary?.Entity.GetType().Name;
            var primaryId = primary is null ? null : TryGetEntityId(primary);

            var actionType = pending switch
            {
                _ when pending.All(p => p.OperationType == AuditOperationType.Insert) => AuditActionType.Create,
                _ when pending.All(p => p.OperationType == AuditOperationType.Delete) => AuditActionType.Delete,
                _ => AuditActionType.Update,
            };

            var header = new AuditEvent
            {
                CorrelationId = _ctx.CorrelationId,
                BusinessId = _ctx.BusinessId,
                OutletId = _ctx.OutletId,
                RealUserId = _ctx.RealUserId,
                ActingUserId = _ctx.ActingUserId,
                RealRoleId = _ctx.RealRoleId,
                ActingRoleId = _ctx.ActingRoleId,
                ActionType = actionType,
                ActionSummary = BuildSummary(actionType, primaryType ?? "Unknown", pending.Count),
                Module = AuditModule.System,
                Source = _ctx.Source,
                PrimaryEntityType = primaryType,
                PrimaryEntityId = primaryId,
                RequestMethod = _ctx.RequestMethod,
                RequestPath = _ctx.RequestPath,
                IpAddress = _ctx.IpAddress,
                RawIp = _ctx.RawIp,
                LocalMachineIp = _ctx.LocalMachineIp,
                IsLocalRequest = _ctx.IsLocalRequest,
                UserAgent = Truncate(_ctx.UserAgent, 500),
                DeviceName = _ctx.DeviceName,
                Browser = _ctx.Browser,
                Os = _ctx.Os,
                Status = AuditStatus.Success,
                CreatedAt = DateTime.UtcNow,
            };

            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                var entry = entries[i];
                var entityRow = new AuditEventEntity
                {
                    EntityType = p.EntityType,
                    EntityId = TryGetEntityId(entry) ?? string.Empty,
                    OperationType = p.OperationType,
                    CreatedAt = DateTime.UtcNow,
                };

                foreach (var f in p.Fields)
                {
                    var sensitive = AuditFieldRedaction.IsSensitive(p.EntityType, f.Name);
                    AuditReferenceSnapshotResolver.ResolvedSnapshot? snapshot = null;
                    if (!sensitive)
                    {
                        try { snapshot = resolver.Resolve(f.Name, f.Old, f.New); }
                        catch (Exception fieldEx)
                        {
                            _logger.LogWarning(fieldEx,
                                "Audit snapshot resolve failed for field {FieldName} on {EntityType}",
                                f.Name, p.EntityType);
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

            db.AuditEvents.Add(header);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Audit save failed — business action was committed but audit record was not persisted. " +
                "Entity={EntityType} Action={ActionType}",
                pending.FirstOrDefault()?.EntityType, pending.FirstOrDefault()?.OperationType);
        }
    }

    private static bool IsAuditEntity(object entity) =>
        entity is AuditEvent or AuditEventEntity or AuditEventFieldChange or RetailPOS.Core.Entities.AuditLog;

    private static string? TryGetEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return null;
        var values = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .Where(v => v is not null)
            .ToList();
        return values.Count == 0 ? null : string.Join(":", values);
    }

    private static string BuildSummary(string action, string entityType, int count) =>
        count == 1
            ? $"{action} {entityType}"
            : $"{action} {entityType} (+{count - 1} related)";

    private static string? ToJson(object? v)
    {
        if (v is null) return null;
        try { return JsonSerializer.Serialize(v); }
        catch { return JsonSerializer.Serialize(v.ToString()); }
    }

    private static string? Truncate(string? s, int max)
    {
        if (s is null) return null;
        return s.Length <= max ? s : s[..max];
    }
}
