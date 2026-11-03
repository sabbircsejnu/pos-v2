using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.Audit;

public sealed class RequiredAuditPostgresTests
{
    [Fact]
    public async Task ProductionProviderCommitsAuditAndBusinessTogether_AndFencesSearchAndDetail()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var settingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"));
            connectionString = new ConfigurationBuilder().AddJsonFile(settingsPath)
                .Build().GetConnectionString("DefaultConnection");
        }
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        await PostgresTestDatabase.RunAsync(connectionString, PostgresTestDatabasePrefix.MigrationBaseline, async database =>
        {
            var auditContext = new AuditContext();
            var failure = new FailAuditInterceptor();
            await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(database.ConnectionString)
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                .AddInterceptors(new AuditChangeTrackerInterceptor(auditContext,
                    NullLogger<AuditChangeTrackerInterceptor>.Instance), failure).Options);
            await db.Database.MigrateAsync();
            db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1, FeatureKey = "test.atomic", IsEnabled = false
            });
            db.AuditEvents.Add(new AuditEvent
            {
                BusinessId = 2, Module = "Payroll", Source = "api", ActionType = "update",
                ActionSummary = "Other Business salary", PrimaryEntityType = "PayrollPayment",
                CorrelationId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
            var hiddenId = (await db.AuditEvents.SingleAsync()).Id;
            auditContext.Initialize(AuditContext.ForBusinessSystemJob("audit-foundation", 1));
            var audit = new AuditService(db, auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
            var transaction = new RequiredAuditTransaction(db, auditContext, audit,
                NullLogger<RequiredAuditTransaction>.Instance);
            var operation = new RequiredAuditOperation(1, "update", "hr", "Foundation", "Employee", "42");

            failure.Fail = true;
            await Assert.ThrowsAsync<IOException>(() => transaction.ExecuteAsync(operation, async token =>
            {
                (await db.BusinessFeatureSettings.SingleAsync(token)).IsEnabled = true;
                await db.SaveChangesAsync(token);
                return true;
            }, CancellationToken.None));
            Assert.False((await db.BusinessFeatureSettings.AsNoTracking().SingleAsync()).IsEnabled);
            Assert.Equal(1, await db.AuditEvents.CountAsync());
            failure.Fail = false;
            await transaction.ExecuteAsync(operation, async token =>
            {
                (await db.BusinessFeatureSettings.SingleAsync(token)).IsEnabled = true;
                return true;
            }, CancellationToken.None);
            Assert.True((await db.BusinessFeatureSettings.AsNoTracking().SingleAsync()).IsEnabled);
            Assert.Equal(1, await db.AuditEvents.CountAsync(row => row.BusinessId == 1));

            var scope = new Mock<IAuditReadAccessScope>();
            scope.Setup(reader => reader.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AuditReadAccess(1, null, true, true, true));
            var store = new AuditEventReadStore(db, scope.Object, transaction);
            Assert.Null(await store.FindDetailsAsync(hiddenId, CancellationToken.None));
            var filter = new AuditEventSearchFilter("Confidential", null, null, null, null, ["hr"], null, null, null);
            var list = await store.ListAsync(new AuditEventListFilter(filter, 1, 20), CancellationToken.None);
            Assert.NotEmpty(list.Items);
            Assert.DoesNotContain(list.Items, row => row.Id == hiddenId);
            var export = await store.ExportAsync(filter, 100, CancellationToken.None);
            Assert.True(export.ConfidentialAccessAudited);
            Assert.DoesNotContain(export.Items, row => row.Id == hiddenId);
        });
    }

    private sealed class FailAuditInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context?.ChangeTracker.Entries<AuditEvent>()
                .Any(entry => entry.State == EntityState.Added) == true)
                throw new IOException("Injected PostgreSQL audit failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
