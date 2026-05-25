using RetailPOS.Core.Audit;

namespace RetailPOS.Infrastructure.Audit;

public sealed class AuditContext : IAuditContext
{
    private AuditContextSnapshot? _snapshot;

    // ── Scope state ──────────────────────────────────────────────────────────
    private bool _scopeActive;
    private string? _scopeActionSummary;
    private string? _scopeActionType;
    private string? _scopeModule;
    private string? _scopePrimaryEntityType;
    private string? _scopePrimaryEntityId;
    private bool _scopeFailed;
    private string? _scopeErrorMessage;
    private readonly List<PendingScopeEntity> _scopeEntities = new();

    public bool IsInitialized => _snapshot is not null;

    public Guid CorrelationId => _snapshot?.CorrelationId ?? Guid.Empty;
    public long? RealUserId => _snapshot?.RealUserId;
    public long? ActingUserId => _snapshot?.ActingUserId;
    public long? RealRoleId => _snapshot?.RealRoleId;
    public long? ActingRoleId => _snapshot?.ActingRoleId;
    public long? OutletId => _snapshot?.OutletId;
    public long? BusinessId => _snapshot?.BusinessId;
    public string Source => _snapshot?.Source ?? Core.Entities.Audit.AuditSource.SystemJob;
    public string? RequestMethod => _snapshot?.RequestMethod;
    public string? RequestPath => _snapshot?.RequestPath;
    public string? IpAddress => _snapshot?.IpAddress;
    public string? RawIp => _snapshot?.RawIp;
    public string? LocalMachineIp => _snapshot?.LocalMachineIp;
    public bool IsLocalRequest => _snapshot?.IsLocalRequest ?? false;
    public string? UserAgent => _snapshot?.UserAgent;
    public string? DeviceName => _snapshot?.DeviceName;
    public string? Browser => _snapshot?.Browser;
    public string? Os => _snapshot?.Os;
    public string? RealUserName => _snapshot?.RealUserName;
    public string? ActingRoleName => _snapshot?.ActingRoleName;

    // ── Scope properties ─────────────────────────────────────────────────────
    public bool HasActiveScope => _scopeActive;
    public string? ScopeActionSummary => _scopeActionSummary;
    public string? ScopeActionType => _scopeActionType;
    public string? ScopeModule => _scopeModule;
    public string? ScopePrimaryEntityType => _scopePrimaryEntityType;
    public string? ScopePrimaryEntityId => _scopePrimaryEntityId;
    public bool ScopeFailed => _scopeFailed;
    public string? ScopeErrorMessage => _scopeErrorMessage;

    public void Initialize(AuditContextSnapshot snapshot) => _snapshot = snapshot;

    public void BeginScope(string actionSummary, string actionType, string module,
        string? primaryEntityType, string? primaryEntityId)
    {
        _scopeActive = true;
        _scopeActionSummary = actionSummary;
        _scopeActionType = actionType;
        _scopeModule = module;
        _scopePrimaryEntityType = primaryEntityType;
        _scopePrimaryEntityId = primaryEntityId;
        _scopeFailed = false;
        _scopeErrorMessage = null;
        _scopeEntities.Clear();
    }

    public void FailScope(string errorMessage)
    {
        _scopeFailed = true;
        _scopeErrorMessage = errorMessage;
    }

    public void EndScope()
    {
        _scopeActive = false;
        _scopeEntities.Clear();
        _scopeActionSummary = null;
        _scopeActionType = null;
        _scopeModule = null;
        _scopePrimaryEntityType = null;
        _scopePrimaryEntityId = null;
        _scopeFailed = false;
        _scopeErrorMessage = null;
    }

    public void AddEntityToScope(PendingScopeEntity entity) => _scopeEntities.Add(entity);

    public IReadOnlyList<PendingScopeEntity> DrainScopeEntities()
    {
        var entities = _scopeEntities.ToList();
        _scopeEntities.Clear();
        return entities;
    }

    public static AuditContextSnapshot ForSystemJob(string jobName, Guid? correlationId = null) =>
        new(
            CorrelationId: correlationId ?? Guid.NewGuid(),
            RealUserId: null,
            ActingUserId: null,
            RealRoleId: null,
            ActingRoleId: null,
            OutletId: null,
            BusinessId: null,
            Source: Core.Entities.Audit.AuditSource.SystemJob,
            RequestMethod: null,
            RequestPath: $"job:{jobName}",
            IpAddress: null,
            RawIp: null,
            LocalMachineIp: null,
            IsLocalRequest: false,
            UserAgent: null,
            DeviceName: null,
            Browser: null,
            Os: null,
            RealUserName: "System",
            ActingRoleName: null);
}
