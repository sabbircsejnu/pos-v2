using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.IntegrationTests.Audit;

public sealed class RequiredAuditFoundationTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext context = new();
    private readonly FailRequiredAuditInterceptor failure = new();
    private RetailPOSDbContext db = null!;
    private AuditService audit = null!;
    private RequiredAuditTransaction transaction = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection).AddInterceptors(
                new AuditChangeTrackerInterceptor(context, NullLogger<AuditChangeTrackerInterceptor>.Instance),
                failure).Options);
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
        {
            Id = 1, BusinessId = 1, FeatureKey = "test.audit", IsEnabled = false
        });
        await db.SaveChangesAsync();
        InitializeActor(1);
        audit = new AuditService(db, context, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
        transaction = new RequiredAuditTransaction(db, context, audit, NullLogger<RequiredAuditTransaction>.Instance);
    }

    [Fact]
    public async Task CriticalWriteAndAuditCommitTogether_ExplicitAndAutomaticEvidenceCreateOneEvent()
    {
        await transaction.ExecuteAsync(Operation(), async token =>
        {
            var setting = await db.BusinessFeatureSettings.SingleAsync(token);
            setting.IsEnabled = true;
            await db.SaveChangesAsync(token);
            await new AuditEventCommandStore(audit).RecordAsync(new AuditEventWriteRequest(
                "update", "hr", "Safe business change", Entities:
                [new AuditEventEntityWriteRequest("Employee", "42", "update", Fields:
                    [new AuditEventFieldWriteRequest("NationalId", "old-private-id", "new-private-id")])]), token);
            return setting.Id;
        }, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.True((await db.BusinessFeatureSettings.SingleAsync()).IsEnabled);
        var header = await db.AuditEvents.Include(row => row.Entities).ThenInclude(row => row.FieldChanges).SingleAsync();
        Assert.Equal(1, header.BusinessId);
        Assert.Equal(101, header.RealUserId);
        Assert.Equal(7, header.ActingRoleId);
        Assert.Equal(context.CorrelationId, header.CorrelationId);
        Assert.Equal(2, header.Entities.Count);
        Assert.Contains(ConfidentialAuditPolicy.RedactedToken,
            header.Entities.Single(entity => entity.EntityType == "Employee").FieldChanges.Single().NewValue);
        await audit.FlushScopeAsync();
        Assert.Equal(1, await db.AuditEvents.CountAsync());
        Assert.False(context.HasActiveScope);
    }

    [Fact]
    public async Task AuditPersistenceFailureRollsBackWrite_AndSameContextRetryHasNoGhostAudit()
    {
        failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => transaction.ExecuteAsync(Operation(), async token =>
        {
            (await db.BusinessFeatureSettings.SingleAsync(token)).IsEnabled = true;
            await db.SaveChangesAsync(token);
            return true;
        }, CancellationToken.None));
        Assert.False((await db.BusinessFeatureSettings.SingleAsync()).IsEnabled);
        Assert.Empty(await db.AuditEvents.ToListAsync());
        Assert.False(context.HasActiveScope);

        failure.Fail = false;
        await transaction.ExecuteAsync(Operation(), async token =>
        {
            (await db.BusinessFeatureSettings.SingleAsync(token)).IsEnabled = true;
            return true;
        }, CancellationToken.None);
        Assert.True((await db.BusinessFeatureSettings.SingleAsync()).IsEnabled);
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task InvalidOrDifferentOperationBusinessIsRejectedBeforeCallback(long businessId)
    {
        var called = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transaction.ExecuteAsync(
            Operation() with { BusinessId = businessId }, _ => { called = true; return Task.FromResult(true); },
            CancellationToken.None));
        Assert.False(called);
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task CrossBusinessEntityCannotBeSavedEvenInsideCallback()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transaction.ExecuteAsync(Operation(), async token =>
        {
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 2, FeatureKey = "test.other", IsEnabled = true
            });
            await db.SaveChangesAsync(token);
            return true;
        }, CancellationToken.None));
        Assert.False(await db.BusinessFeatureSettings.AnyAsync(row => row.BusinessId == 2));
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task BusinessScopedJobCommits_WhileGenericUnscopedJobIsRejected()
    {
        context.Initialize(AuditContext.ForSystemJob("unscoped"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            transaction.ExecuteAsync(Operation(), _ => Task.FromResult(true), CancellationToken.None));
        context.Initialize(AuditContext.ForBusinessSystemJob("foundation-job", 1));
        await transaction.ExecuteAsync(Operation(), _ => Task.FromResult(true), CancellationToken.None);
        var header = await db.AuditEvents.SingleAsync();
        Assert.Equal(1, header.BusinessId);
        Assert.Equal(AuditSource.SystemJob, header.Source);
        Assert.Equal("job:foundation-job", header.RequestPath);
        Assert.Null(header.RealUserId);
        Assert.NotEqual(Guid.Empty, header.CorrelationId);
    }

    [Fact]
    public async Task NestedBoundaryAndPrematureFlushAreRejectedWithoutCommittingBusinessState()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(Operation(), async token =>
        {
            (await db.BusinessFeatureSettings.SingleAsync(token)).IsEnabled = true;
            await db.SaveChangesAsync(token);
            await transaction.ExecuteAsync(Operation(), _ => Task.FromResult(true), token);
            return true;
        }, CancellationToken.None));
        Assert.False((await db.BusinessFeatureSettings.SingleAsync()).IsEnabled);
        Assert.Empty(await db.AuditEvents.ToListAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(Operation(), async token =>
        {
            (await db.BusinessFeatureSettings.SingleAsync(token)).IsEnabled = true;
            await audit.FlushScopeAsync(token);
            return true;
        }, CancellationToken.None));
        Assert.False((await db.BusinessFeatureSettings.SingleAsync()).IsEnabled);
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Theory]
    [InlineData("hr", "Employee")]
    [InlineData("payroll", "PayrollPayment")]
    [InlineData("system", "Payslip")]
    public async Task HistoricalConfidentialReadsAreFencedAndMaskedBeforeReturn(string module, string entity)
    {
        await SeedConfidentialAsync(module, entity);
        var scope = new Mock<IAuditReadAccessScope>();
        scope.Setup(access => access.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditReadAccess(1, null, true, true, true));
        var store = new AuditEventReadStore(db, scope.Object, transaction);
        var details = await store.FindDetailsAsync(10, CancellationToken.None);
        Assert.NotNull(details);
        Assert.DoesNotContain("private", details.ListItem.ActionSummary);
        Assert.Null(details.RequestPath);
        Assert.Equal(ConfidentialAuditPolicy.RedactedToken, details.ErrorMessage);
        var field = details.AffectedEntities.Single().FieldChanges.Single();
        Assert.Contains(ConfidentialAuditPolicy.RedactedToken, field.NewValue);
        Assert.Null(field.NewDisplayValue);
        Assert.Null(field.OldDisplayValue);
        Assert.NotNull(await db.AuditEvents.SingleOrDefaultAsync(row =>
            row.PrimaryEntityType == "ConfidentialAuditAccess" && row.ActionType == "detail"));

        Assert.Null(await store.FindDetailsAsync(20, CancellationToken.None));
        var list = await store.ListAsync(new AuditEventListFilter(Filter(), 1, 50), CancellationToken.None);
        Assert.DoesNotContain(list.Items, item => item.Id == 20);
        Assert.DoesNotContain(list.Items, item => item.ActionSummary.Contains("private"));
        var export = await store.ExportAsync(Filter(), 100, CancellationToken.None);
        Assert.DoesNotContain(export.Items, item => item.Id == 20);
        Assert.DoesNotContain(export.Items, item =>
            item.ActionSummary.Contains("private") || item.RequestPath?.Contains("private") == true);
    }

    [Fact]
    public async Task MissingPermissionsUnscopedAndWrongLocationCannotReadConfidentialDetail()
    {
        await SeedConfidentialAsync("payroll", "PayrollPayment");
        var scope = new Mock<IAuditReadAccessScope>();
        var store = new AuditEventReadStore(db, scope.Object, transaction);
        foreach (var access in new[]
        {
            new AuditReadAccess(1, null, true, false, false),
            new AuditReadAccess(null, null, true, true, true),
            new AuditReadAccess(1, 99, false, true, true)
        })
        {
            scope.Setup(reader => reader.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(access);
            Assert.Null(await store.FindDetailsAsync(10, CancellationToken.None));
            var export = await store.ExportAsync(Filter() with { Modules = ["payroll"] }, 100, CancellationToken.None);
            Assert.Empty(export.Items);
        }
    }

    [Theory]
    [InlineData("detail")]
    [InlineData("list")]
    [InlineData("export")]
    public async Task RequiredAccessAuditFailureDoesNotReturnConfidentialContent(string action)
    {
        await SeedConfidentialAsync("payroll", "PayrollPayment");
        var scope = new Mock<IAuditReadAccessScope>();
        scope.Setup(reader => reader.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditReadAccess(1, null, true, true, true));
        var store = new AuditEventReadStore(db, scope.Object, transaction);
        failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            if (action == "detail") await store.FindDetailsAsync(10, CancellationToken.None);
            else if (action == "list") await store.ListAsync(new AuditEventListFilter(Filter(), 1, 50), CancellationToken.None);
            else await store.ExportAsync(Filter(), 100, CancellationToken.None);
        });
        Assert.Equal(2, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task NonConfidentialLegacyReadStillWorksWithoutBusinessOrSensitivePermissions()
    {
        context.EndScope();
        db.AuditEvents.Add(new AuditEvent
        {
            Id = 10, BusinessId = 2, Module = "inventory", ActionType = "update",
            ActionSummary = "Updated product", Source = "api", CorrelationId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
        var scope = new Mock<IAuditReadAccessScope>();
        scope.Setup(reader => reader.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditReadAccess(null, null, false, false, false));
        var details = await new AuditEventReadStore(db, scope.Object, transaction).FindDetailsAsync(10, CancellationToken.None);
        Assert.Equal("Updated product", details?.ListItem.ActionSummary);
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Theory]
    [InlineData("Employee", "NationalId")]
    [InlineData("Employee", "PassportNumber")]
    [InlineData("EmployeeDocument", "StorageKey")]
    [InlineData("EmployeeDocument", "FileName")]
    [InlineData("SalaryRevision", "BasicSalary")]
    [InlineData("PayrollPayment", "BankAccountNumber")]
    [InlineData("PayrollPayment", "MobilePaymentNumber")]
    [InlineData("Payslip", "DocumentContent")]
    [InlineData("PayrollExport", "Payload")]
    public async Task ConfidentialWriteNeverPersistsRawFieldsMetadataSummariesOrPaths(string entityType, string field)
    {
        await audit.RecordAsync(new AuditEventInput
        {
            ActionType = "update", Module = entityType.StartsWith("Employee") ? "hr" : "payroll",
            Summary = "private-summary", PrimaryEntity = (entityType, "42"),
            Metadata = new Dictionary<string, object?> { ["payload"] = "private-metadata" },
            Entities = [new AuditEntityInput(entityType, "42", "update",
                [(field, "private-old", "private-new")], metadata: "private-entity-metadata")]
        });
        var header = await db.AuditEvents.Include(row => row.Entities).ThenInclude(entity => entity.FieldChanges).SingleAsync();
        Assert.DoesNotContain("private", header.ActionSummary);
        Assert.Null(header.Metadata);
        Assert.Null(header.RequestPath);
        var entity = Assert.Single(header.Entities);
        Assert.Null(entity.Metadata);
        var change = Assert.Single(entity.FieldChanges);
        Assert.Equal("\"***REDACTED***\"", change.OldValue);
        Assert.Equal("\"***REDACTED***\"", change.NewValue);
    }

    private async Task SeedConfidentialAsync(string module, string entity)
    {
        foreach (var (id, businessId) in new[] { (10L, 1L), (20L, 2L) })
            db.AuditEvents.Add(new AuditEvent
            {
                Id = id, BusinessId = businessId, Module = module, ActionType = "update",
                ActionSummary = "private salary value 99999", Source = "api", CorrelationId = Guid.NewGuid(),
                PrimaryEntityType = entity, PrimaryEntityId = "42", ErrorMessage = "private bank details",
                RequestPath = "/api/private?bank=123",
                Entities = [new AuditEventEntity
                {
                    EntityType = entity, EntityId = "42", OperationType = "update", FieldsChangedCount = 1,
                    FieldChanges = [new AuditEventFieldChange
                    {
                        FieldName = "BankAccountNumber", OldValue = "\"private-old\"",
                        NewValue = "\"private-new\"", OldDisplayValue = "private-old-name",
                        NewDisplayValue = "private-new-name"
                    }]
                }]
            });
        await db.SaveChangesAsync();
    }

    private static RequiredAuditOperation Operation() => new(1, "update", "hr", "Foundation test", "Employee", "42");
    private static AuditEventSearchFilter Filter() => new(null, null, null, null, null, null, null, null, null);

    private void InitializeActor(long businessId) => context.Initialize(new AuditContextSnapshot(
        Guid.NewGuid(), 101, 101, 5, 7, null, businessId, AuditSource.API,
        "POST", "/api/hr/v1/foundation", null, null, null, false, null, null, null, null, "tester", "HR"));

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private sealed class FailRequiredAuditInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context?.ChangeTracker.Entries<AuditEvent>()
                .Any(entry => entry.State == EntityState.Added) == true)
                throw new IOException("Injected required audit failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
