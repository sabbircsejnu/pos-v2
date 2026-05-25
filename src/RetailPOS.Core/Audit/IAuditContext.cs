namespace RetailPOS.Core.Audit;

public interface IAuditContext
{
    Guid CorrelationId { get; }

    long? RealUserId { get; }
    long? ActingUserId { get; }
    long? RealRoleId { get; }
    long? ActingRoleId { get; }
    long? OutletId { get; }
    long? BusinessId { get; }

    string Source { get; }
    string? RequestMethod { get; }
    string? RequestPath { get; }
    string? IpAddress { get; }
    string? RawIp { get; }
    string? LocalMachineIp { get; }
    bool IsLocalRequest { get; }
    string? UserAgent { get; }
    string? DeviceName { get; }
    string? Browser { get; }
    string? Os { get; }

    string? RealUserName { get; }
    string? ActingRoleName { get; }

    bool IsInitialized { get; }

    void Initialize(AuditContextSnapshot snapshot);

    // ── Audit Scope (one business action = one top-level event) ─────────────
    bool HasActiveScope { get; }
    string? ScopeActionSummary { get; }
    string? ScopeActionType { get; }
    string? ScopeModule { get; }
    string? ScopePrimaryEntityType { get; }
    string? ScopePrimaryEntityId { get; }
    bool ScopeFailed { get; }
    string? ScopeErrorMessage { get; }

    /// <summary>
    /// Begin a named business-level audit scope. All EF entity changes that occur
    /// while the scope is active are accumulated and flushed as a single AuditEvent
    /// when <see cref="IAuditService.FlushScopeAsync"/> is called.
    /// </summary>
    void BeginScope(string actionSummary, string actionType, string module,
        string? primaryEntityType, string? primaryEntityId);

    /// <summary>Marks the active scope as failed with an error message.</summary>
    void FailScope(string errorMessage);

    /// <summary>Ends and clears the scope without persisting (use after a failed operation).</summary>
    void EndScope();

    /// <summary>Adds an entity change to the active scope's accumulator.</summary>
    void AddEntityToScope(PendingScopeEntity entity);

    /// <summary>Returns all accumulated entities and clears the internal list.</summary>
    IReadOnlyList<PendingScopeEntity> DrainScopeEntities();
}

/// <summary>
/// An entity change captured by the EF interceptor while an audit scope is active.
/// Fields are stored as raw values; display-name resolution is deferred to flush time.
/// </summary>
public sealed record PendingScopeEntity(
    string EntityType,
    string EntityId,
    string OperationType,
    IReadOnlyList<(string Name, object? Old, object? New)> Fields,
    bool IsInternalOperation,
    string? InternalOperationName);

public sealed record AuditContextSnapshot(
    Guid CorrelationId,
    long? RealUserId,
    long? ActingUserId,
    long? RealRoleId,
    long? ActingRoleId,
    long? OutletId,
    long? BusinessId,
    string Source,
    string? RequestMethod,
    string? RequestPath,
    string? IpAddress,
    string? RawIp,
    string? LocalMachineIp,
    bool IsLocalRequest,
    string? UserAgent,
    string? DeviceName,
    string? Browser,
    string? Os,
    string? RealUserName,
    string? ActingRoleName);
