using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Employees;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrEmployeeDocumentPostgresTests
{
    [Fact]
    public void LegacyTimestampRuntimeModelMatchesDesignTimeSnapshotAndLatestMigration()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        using var runtime = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql("Host=localhost;Database=model_parity;Username=model_parity")
            .Options);
        using var designTime = new RetailPOSDbContextFactory().CreateDbContext([]);
        var migrations = runtime.GetService<IMigrationsAssembly>();
        var latest = migrations.Migrations.OrderBy(row => row.Key, StringComparer.Ordinal).Last();
        Assert.Contains(migrations.Migrations.Keys, id => id.EndsWith("_Phase1HEmployeeDocuments", StringComparison.Ordinal));
        var latestMigration = migrations.CreateMigration(latest.Value, runtime.Database.ProviderName!);

        AssertModelMatches(runtime, designTime.GetService<IDesignTimeModel>().Model);
        AssertModelMatches(runtime, migrations.ModelSnapshot!.Model);
        AssertModelMatches(runtime, latestMigration.TargetModel);
        Assert.False(runtime.Database.HasPendingModelChanges());
        Assert.False(designTime.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task DocumentTypesAndImmutableVersionsAreBusinessBound()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var database = await PostgresTestDatabase.CreateAsync(
            GetPostgresConnectionString(), PostgresTestDatabasePrefix.MigrationBaseline);
        try
        {
            var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(database.ConnectionString)
                .Options;
            long employeeId;
            long documentTypeId;
            await using (var setup = new RetailPOSDbContext(options))
            {
                Assert.False(setup.Database.HasPendingModelChanges());
                await setup.Database.MigrateAsync();
                var expectedMigrations = setup.Database.GetMigrations().ToArray();
                Assert.Equal(expectedMigrations, (await setup.Database.GetAppliedMigrationsAsync()).ToArray());
                Assert.Empty(await setup.Database.GetPendingMigrationsAsync());
                await using (var current = new RetailPOSDbContext(options))
                {
                    await current.Database.MigrateAsync();
                    Assert.Equal(expectedMigrations, (await current.Database.GetAppliedMigrationsAsync()).ToArray());
                    Assert.Empty(await current.Database.GetPendingMigrationsAsync());
                }
                var business = new Business
                {
                    Id = 87231,
                    Name = "Employee document provider test",
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };
                setup.Businesses.AddRange(business, new Business
                {
                    Id = 87232,
                    Name = "Other employee document business",
                    CreatedAt = business.CreatedAt,
                    UpdatedAt = business.UpdatedAt
                });
                var type = new HrEmploymentType
                {
                    BusinessId = business.Id,
                    Code = "Permanent",
                    Name = "Permanent"
                };
                var otherType = new HrEmploymentType
                {
                    BusinessId = 87232,
                    Code = "Permanent",
                    Name = "Permanent"
                };
                setup.HrEmploymentTypes.AddRange(type, otherType);
                await setup.SaveChangesAsync();
                var employee = new HrEmployee
                {
                    BusinessId = business.Id,
                    EmployeeCode = "DOC-EMP-1",
                    FirstName = "Employee",
                    DisplayName = "Employee Document Test",
                    CurrentEmploymentStatus = "Active",
                    CurrentEmploymentTypeId = type.Id,
                    JoiningDate = new DateOnly(2026, 10, 1)
                };
                setup.HrEmployees.Add(employee);
                await setup.SaveChangesAsync();
                employeeId = employee.Id;
                var store = new HrEmployeeDocumentStore(setup, TimeProvider.System);
                var documentType = await store.CreateTypeAsync(business.Id,
                    new HrEmployeeDocumentTypeData(
                        "IDENTITY", "Identity document", "Confidential", true, 365, false, true),
                    CancellationToken.None);
                documentTypeId = documentType.Id;
                Assert.Equal("IDENTITY", documentType.Code);
                Assert.True(documentType.RequiresExpiryDate);
            }

            var documentId = 0L;
            var versionId = Guid.NewGuid();
            await using (var context = new RetailPOSDbContext(options))
            {
                var store = new HrEmployeeDocumentStore(context, TimeProvider.System);
                var document = await store.CreateAsync(
                    87231, employeeId, documentTypeId, 101, "ID-001",
                    new DateOnly(2026, 10, 1), new DateOnly(2027, 10, 1), null, false,
                    CancellationToken.None);
                documentId = document.Id;
                await store.AddAsync(new ConfidentialFileVersion(
                    versionId, 87231, document.Id, 1, ConfidentialFileClassification.EmployeeDocument,
                    "opaque/private-key", "identity.pdf", "application/pdf", 2,
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2])),
                    "test-encryption-key", ConfidentialFileScanStatus.Clean, null, null, null,
                    UploadedByUserId: 101, ExpectedSubjectVersion: document.Version),
                    CancellationToken.None);

                var saved = await store.GetAsync(87231, document.Id, CancellationToken.None);
                Assert.NotNull(saved);
                Assert.Equal(versionId, saved.CurrentVersionId);
                Assert.Single(saved.Versions);
                Assert.Equal("Clean", saved.Versions[0].ScanStatus);
                Assert.DoesNotContain("opaque/private-key",
                    System.Text.Json.JsonSerializer.Serialize(saved), StringComparison.Ordinal);
            }

            await using (var context = new RetailPOSDbContext(options))
            {
                var version = await context.HrEmployeeDocumentVersions.SingleAsync(row => row.Id == versionId);
                version.ContentType = "text/plain";
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            }

            await using (var context = new RetailPOSDbContext(options))
            {
                var store = new HrEmployeeDocumentStore(context, TimeProvider.System);
                var otherType = await store.CreateTypeAsync(87232,
                    new HrEmployeeDocumentTypeData(
                        "PASSPORT", "Passport", "Restricted", false, null, false, true),
                    CancellationToken.None);
                await Assert.ThrowsAsync<DbUpdateException>(() => store.CreateAsync(
                    87232, employeeId, otherType.Id, 101, null, null, null, null, false,
                    CancellationToken.None));
                Assert.Null(await store.GetAsync(87232, documentId, CancellationToken.None));
            }
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static void AssertModelMatches(RetailPOSDbContext context, IModel expectedModel)
    {
        var expected = context.GetService<IModelRuntimeInitializer>()
            .Initialize(expectedModel, designTime: true).GetRelationalModel();
        var actual = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(expected, actual);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine,
            differences.Select(operation => operation is AlterColumnOperation column
                ? $"{column.Table}.{column.Name}: {column.OldColumn.ColumnType} -> {column.ColumnType}"
                : operation.GetType().Name)));
    }

    private static string GetPostgresConnectionString()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configuredConnection))
            return configuredConnection;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("PostgreSQL test connection is not configured.");
    }
}
