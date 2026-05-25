using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using Xunit;

namespace RetailPOS.Tests.Audit;

/// <summary>
/// Tests that verify audit scope grouping: one business action = one top-level AuditEvent.
/// </summary>
public class AuditScopeTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly AuditContext _auditContext;
    private readonly AuditService _auditService;

    public AuditScopeTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new RetailPOSDbContext(options);

        _auditContext = new AuditContext();
        _auditContext.Initialize(new AuditContextSnapshot(
            CorrelationId: Guid.NewGuid(),
            RealUserId: 1L,
            ActingUserId: 1L,
            RealRoleId: 1L,
            ActingRoleId: 1L,
            OutletId: 10L,
            BusinessId: 5L,
            Source: AuditSource.API,
            RequestMethod: "POST",
            RequestPath: "/api/purchase-orders/5/submit",
            IpAddress: "127.0.0.1",
            RawIp: "127.0.0.1",
            LocalMachineIp: null,
            IsLocalRequest: false,
            UserAgent: null,
            DeviceName: null,
            Browser: null,
            Os: null,
            RealUserName: "testuser",
            ActingRoleName: "Admin"));

        var spMock = new Mock<IServiceProvider>();
        _auditService = new AuditService(_db, _auditContext, spMock.Object,
            NullLogger<AuditService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── BeginScope / EndScope ────────────────────────────────────────────────

    [Fact]
    public void BeginScope_SetsActiveScope()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");

        Assert.True(_auditContext.HasActiveScope);
        Assert.Equal("Submit Purchase Order", _auditContext.ScopeActionSummary);
        Assert.Equal(AuditActionType.Submit, _auditContext.ScopeActionType);
        Assert.Equal(AuditModule.Purchase, _auditContext.ScopeModule);
        Assert.Equal("PurchaseOrder", _auditContext.ScopePrimaryEntityType);
        Assert.Equal("5", _auditContext.ScopePrimaryEntityId);
        Assert.False(_auditContext.ScopeFailed);
    }

    [Fact]
    public void EndScope_ClearsActiveScope()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");

        _auditContext.EndScope();

        Assert.False(_auditContext.HasActiveScope);
        Assert.Null(_auditContext.ScopeActionSummary);
    }

    [Fact]
    public void FailScope_MarksAsFailed()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");

        _auditContext.FailScope("Something went wrong");

        Assert.True(_auditContext.HasActiveScope);
        Assert.True(_auditContext.ScopeFailed);
        Assert.Equal("Something went wrong", _auditContext.ScopeErrorMessage);
    }

    // ── AddEntityToScope / DrainScopeEntities ────────────────────────────────

    [Fact]
    public void AddEntityToScope_AccumulatesEntities()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");

        _auditContext.AddEntityToScope(new PendingScopeEntity(
            "PurchaseOrder", "5", AuditOperationType.Update,
            new[] { ("Status", (object?)"draft", (object?)"pending") },
            IsInternalOperation: true, InternalOperationName: null));

        var drained = _auditContext.DrainScopeEntities();

        Assert.Single(drained);
        Assert.Equal("PurchaseOrder", drained[0].EntityType);
        Assert.True(drained[0].IsInternalOperation);
    }

    [Fact]
    public void DrainScopeEntities_ClearsAccumulator()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");

        _auditContext.AddEntityToScope(new PendingScopeEntity(
            "PurchaseOrder", "5", AuditOperationType.Update,
            Array.Empty<(string, object?, object?)>(),
            IsInternalOperation: true, InternalOperationName: null));

        _auditContext.DrainScopeEntities(); // first drain
        var second = _auditContext.DrainScopeEntities(); // second drain should be empty

        Assert.Empty(second);
    }

    // ── FlushScopeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task FlushScopeAsync_NoScope_DoesNothing()
    {
        Assert.False(_auditContext.HasActiveScope);

        await _auditService.FlushScopeAsync();

        Assert.Equal(0, await _db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task FlushScopeAsync_CreatesOneAuditEvent_WithBusinessSummary()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");

        _auditContext.AddEntityToScope(new PendingScopeEntity(
            "PurchaseOrder", "5", AuditOperationType.Update,
            new[] { ("Status", (object?)"draft", (object?)"pending") },
            IsInternalOperation: true, InternalOperationName: null));

        await _auditService.FlushScopeAsync();

        var events = await _db.AuditEvents
            .Include(e => e.Entities)
            .ThenInclude(en => en.FieldChanges)
            .ToListAsync();

        // Exactly one top-level audit event
        Assert.Single(events);
        var ev = events[0];
        Assert.Equal("Submit Purchase Order", ev.ActionSummary);
        Assert.Equal(AuditActionType.Submit, ev.ActionType);
        Assert.Equal(AuditModule.Purchase, ev.Module);
        Assert.Equal("PurchaseOrder", ev.PrimaryEntityType);
        Assert.Equal("5", ev.PrimaryEntityId);
        Assert.Equal(AuditStatus.Success, ev.Status);
    }

    [Fact]
    public async Task FlushScopeAsync_EntityIsMarkedInternalOperation()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");

        _auditContext.AddEntityToScope(new PendingScopeEntity(
            "PurchaseOrder", "5", AuditOperationType.Update,
            new[] { ("Status", (object?)"draft", (object?)"pending") },
            IsInternalOperation: true, InternalOperationName: null));

        await _auditService.FlushScopeAsync();

        var entity = await _db.AuditEventEntities.SingleAsync();
        Assert.True(entity.IsInternalOperation);
    }

    [Fact]
    public async Task FlushScopeAsync_ClearsScope_AfterFlush()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");

        _auditContext.AddEntityToScope(new PendingScopeEntity(
            "PurchaseOrder", "5", AuditOperationType.Update,
            Array.Empty<(string, object?, object?)>(),
            IsInternalOperation: true, InternalOperationName: null));

        await _auditService.FlushScopeAsync();

        Assert.False(_auditContext.HasActiveScope);
    }

    [Fact]
    public async Task FlushScopeAsync_FailedScope_RecordsFailedStatus()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");
        _auditContext.FailScope("PO not in draft status");

        await _auditService.FlushScopeAsync();

        var ev = await _db.AuditEvents.SingleAsync();
        Assert.Equal(AuditStatus.Failed, ev.Status);
        Assert.Equal("PO not in draft status", ev.ErrorMessage);
    }

    [Fact]
    public async Task FlushScopeAsync_MultipleEntities_AllGroupedUnderOneEvent()
    {
        _auditContext.BeginScope("Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", "5");

        _auditContext.AddEntityToScope(new PendingScopeEntity(
            "PurchaseOrder", "5", AuditOperationType.Update,
            new[] { ("Status", (object?)"draft", (object?)"pending") },
            IsInternalOperation: true, InternalOperationName: null));

        _auditContext.AddEntityToScope(new PendingScopeEntity(
            "PurchaseOrderItem", "11", AuditOperationType.Update,
            Array.Empty<(string, object?, object?)>(),
            IsInternalOperation: true, InternalOperationName: null));

        await _auditService.FlushScopeAsync();

        var eventCount = await _db.AuditEvents.CountAsync();
        var entityCount = await _db.AuditEventEntities.CountAsync();

        Assert.Equal(1, eventCount);   // one business event
        Assert.Equal(2, entityCount);  // two affected entities under it
    }

    // ── No scope → standalone event (existing behaviour preserved) ───────────

    [Fact]
    public async Task RecordAsync_WithoutScope_CreatesStandaloneEvent()
    {
        Assert.False(_auditContext.HasActiveScope);

        await _auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Purchase,
            Summary = "Update PurchaseOrder",
        });

        var ev = await _db.AuditEvents.SingleAsync();
        Assert.Equal("Update PurchaseOrder", ev.ActionSummary);
    }
}
