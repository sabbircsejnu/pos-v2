using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using Xunit;

namespace RetailPOS.Tests.Audit;

public class AuditServiceTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<IAuditContext> _ctxMock;
    private readonly Mock<IServiceProvider> _spMock;
    private readonly AuditService _sut;

    public AuditServiceTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new RetailPOSDbContext(options);

        _ctxMock = new Mock<IAuditContext>();
        _ctxMock.Setup(c => c.IsInitialized).Returns(true);
        _ctxMock.Setup(c => c.CorrelationId).Returns(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        _ctxMock.Setup(c => c.RealUserId).Returns(42L);
        _ctxMock.Setup(c => c.ActingUserId).Returns(42L);
        _ctxMock.Setup(c => c.RealRoleId).Returns(1L);
        _ctxMock.Setup(c => c.ActingRoleId).Returns(1L);
        _ctxMock.Setup(c => c.OutletId).Returns(10L);
        _ctxMock.Setup(c => c.BusinessId).Returns(5L);
        _ctxMock.Setup(c => c.Source).Returns(AuditSource.API);
        _ctxMock.Setup(c => c.RequestMethod).Returns("POST");
        _ctxMock.Setup(c => c.RequestPath).Returns("/api/products");
        _ctxMock.Setup(c => c.IpAddress).Returns("127.0.0.1");
        _ctxMock.Setup(c => c.RawIp).Returns("127.0.0.1");
        _ctxMock.Setup(c => c.LocalMachineIp).Returns("192.168.1.10");
        _ctxMock.Setup(c => c.IsLocalRequest).Returns(true);
        _ctxMock.Setup(c => c.UserAgent).Returns((string?)null);

        _spMock = new Mock<IServiceProvider>();

        _sut = new AuditService(_db, _ctxMock.Object, _spMock.Object,
            NullLogger<AuditService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RecordAsync_PersistsEventWithContextValues()
    {
        var input = new AuditEventInput
        {
            ActionType = AuditActionType.Create,
            Module = AuditModule.Inventory,
            Summary = "Created product Widget",
        };

        await _sut.RecordAsync(input);

        var ev = await _db.AuditEvents.SingleAsync();
        Assert.Equal(AuditActionType.Create, ev.ActionType);
        Assert.Equal(AuditModule.Inventory, ev.Module);
        Assert.Equal("Created product Widget", ev.ActionSummary);
        Assert.Equal(42L, ev.RealUserId);
        Assert.Equal(1L, ev.RealRoleId);
        Assert.Equal(10L, ev.OutletId);
        Assert.Equal(5L, ev.BusinessId);
        Assert.Equal(AuditStatus.Success, ev.Status);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), ev.CorrelationId);
    }

    [Fact]
    public async Task RecordAsync_AttachesEntitiesWithFieldChanges()
    {
        var fields = new List<(string FieldName, object? OldValue, object? NewValue)>
        {
            ("Name", "OldName", "NewName"),
            ("Price", 10m, 20m),
        };
        var input = new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Inventory,
            Summary = "Updated product",
            Entities = new[]
            {
                new AuditEntityInput("Product", "99", AuditOperationType.Update, fields),
            },
        };

        await _sut.RecordAsync(input);

        var ev = await _db.AuditEvents.Include(e => e.Entities).ThenInclude(e => e.FieldChanges).SingleAsync();
        Assert.Single(ev.Entities);
        var entity = ev.Entities.Single();
        Assert.Equal("Product", entity.EntityType);
        Assert.Equal("99", entity.EntityId);
        Assert.Equal(AuditOperationType.Update, entity.OperationType);
        Assert.Equal(2, entity.FieldsChangedCount);
        Assert.Equal(2, entity.FieldChanges.Count);
    }

    [Fact]
    public async Task RecordAsync_SensitiveField_IsRedacted()
    {
        var fields = new List<(string FieldName, object? OldValue, object? NewValue)>
        {
            ("PasswordHash", "old-hash", "new-hash"),
        };
        var input = new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Admin,
            Summary = "Password changed",
            Entities = new[]
            {
                new AuditEntityInput("User", "1", AuditOperationType.Update, fields),
            },
        };

        await _sut.RecordAsync(input);

        var ev = await _db.AuditEvents.Include(e => e.Entities).ThenInclude(e => e.FieldChanges).SingleAsync();
        var fc = ev.Entities.Single().FieldChanges.Single();
        Assert.Equal("PasswordHash", fc.FieldName);
        Assert.Contains(AuditFieldRedaction.RedactedToken, fc.OldValue);
        Assert.Contains(AuditFieldRedaction.RedactedToken, fc.NewValue);
    }

    [Fact]
    public void BuildPendingEvent_NullSummary_UsesEmptyString()
    {
        var ev = _sut.BuildPendingEvent(AuditActionType.Create, AuditModule.System, null!, null, null);

        Assert.Equal(string.Empty, ev.ActionSummary);
    }

    [Fact]
    public void BuildPendingEvent_LongSummary_TruncatesTo500()
    {
        var longSummary = new string('x', 600);

        var ev = _sut.BuildPendingEvent(AuditActionType.Create, AuditModule.System, longSummary, null, null);

        Assert.Equal(500, ev.ActionSummary.Length);
    }

    [Fact]
    public void BuildPendingEvent_NullUserAgent_StoredAsNull()
    {
        _ctxMock.Setup(c => c.UserAgent).Returns((string?)null);

        var ev = _sut.BuildPendingEvent(AuditActionType.Login, AuditModule.Auth, "Login", null, null);

        Assert.Null(ev.UserAgent);
    }

    [Fact]
    public async Task RecordAsync_WithExplicitStatus_OverridesDefault()
    {
        var input = new AuditEventInput
        {
            ActionType = AuditActionType.Login,
            Module = AuditModule.Auth,
            Summary = "Login attempt",
            Status = AuditStatus.Failed,
            ErrorMessage = "Invalid credentials",
        };

        await _sut.RecordAsync(input);

        var ev = await _db.AuditEvents.SingleAsync();
        Assert.Equal(AuditStatus.Failed, ev.Status);
        Assert.Equal("Invalid credentials", ev.ErrorMessage);
    }
}
