using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.IntegrationTests.HR;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.Audit;

public sealed class DatabaseProtectionPostgresTests
{
    [Fact]
    public async Task IdempotentMigrationScriptExecutesOnCleanDatabaseAndCanBeReapplied()
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
        await PostgresTestDatabase.RunAsync(connectionString!, PostgresTestDatabasePrefix.MigrationBaseline, PostgresTestDatabaseStrategy.FreshMigration, async database =>
        {
            await using var db = CreateContext(database.ConnectionString);
            var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using (var command = new NpgsqlCommand(script, connection) { CommandTimeout = 600 })
            {
                await command.ExecuteNonQueryAsync();
            }

            var migrations = db.Database.GetMigrations().ToArray();
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Equal(migrations, applied);
            Assert.Contains("20261013041308_RestoreAuditAndWalkInDatabaseProtections", applied);

            await using (var command = new NpgsqlCommand(script, connection) { CommandTimeout = 600 })
            {
                await command.ExecuteNonQueryAsync();
            }

            Assert.Equal(migrations, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        });
    }

    [Fact]
    public async Task AuditRowsAreImmutable_AndSystemCustomersAreProtected()
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
        await PostgresTestDatabase.RunAsync(connectionString!, PostgresTestDatabasePrefix.MigrationBaseline, PostgresTestDatabaseStrategy.FreshMigration, async database =>
        {
            await using var db = CreateContext(database.ConnectionString);
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO audit_events
                    (id, correlation_id, action_type, action_summary, module, source, status, is_local_request, created_at)
                VALUES (1, gen_random_uuid(), 'update', 'test event', 'Audit', 'test', 'Success', FALSE, CURRENT_TIMESTAMP);
                INSERT INTO audit_event_entities
                    (id, audit_event_id, entity_type, entity_id, operation_type, fields_changed_count, created_at)
                VALUES (1, 1, 'Customer', '1', 'Update', 1, CURRENT_TIMESTAMP);
                INSERT INTO audit_event_field_changes
                    (id, audit_event_entity_id, field_name, created_at)
                VALUES (1, 1, 'name', CURRENT_TIMESTAMP);
                INSERT INTO businesses (id, name, is_active, created_at, updated_at)
                VALUES (1, 'Protection Business', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                INSERT INTO customers
                    (id, business_id, name, customer_code, is_system, is_active, loyalty_points, created_at)
                VALUES
                    (1, 1, 'Walk-in Customer', 'WALKIN', TRUE, TRUE, 0, CURRENT_TIMESTAMP),
                    (2, 1, 'Regular Customer', 'REGULAR', FALSE, TRUE, 0, CURRENT_TIMESTAMP);
                """);

            await AssertBlockedAsync(db, "UPDATE audit_events SET action_summary = 'changed' WHERE id = 1");
            await AssertBlockedAsync(db, "DELETE FROM audit_events WHERE id = 1");
            await AssertBlockedAsync(db, "UPDATE audit_event_entities SET entity_id = 'changed' WHERE id = 1");
            await AssertBlockedAsync(db, "DELETE FROM audit_event_entities WHERE id = 1");
            await AssertBlockedAsync(db, "UPDATE audit_event_field_changes SET field_name = 'changed' WHERE id = 1");
            await AssertBlockedAsync(db, "DELETE FROM audit_event_field_changes WHERE id = 1");
            await AssertBlockedAsync(db, "UPDATE customers SET name = 'Changed Walk-in' WHERE id = 1");
            await AssertBlockedAsync(db, "DELETE FROM customers WHERE id = 1");

            await db.Database.ExecuteSqlRawAsync("UPDATE customers SET name = 'Updated Regular' WHERE id = 2");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM customers WHERE id = 2");
            Assert.Equal(1, await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM customers WHERE id = 1").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM customers WHERE id = 2").SingleAsync());
        });
    }

    [Fact]
    public async Task FinalSettlementEvidenceIsImmutable_AndHeaderAdvancesSequentially()
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
        await PostgresTestDatabase.RunAsync(connectionString!, PostgresTestDatabasePrefix.MigrationBaseline, PostgresTestDatabaseStrategy.FreshMigration, async database =>
        {
            await using var db = CreateContext(database.ConnectionString);
            await db.Database.MigrateAsync();
            db.Businesses.AddRange(
                new Business { Id = 1, Name = "Final settlement" },
                new Business { Id = 2, Name = "Other" });
            await db.SaveChangesAsync();
            await HrRosterPersistenceTests.Seed(db, businessExists: true);

            var template = new HrClearanceTemplate
            {
                BusinessId = 1, Code = "SETTLEMENT", Name = "Settlement",
                ScopeType = HrClearanceTemplateScopeType.Business, VersionNo = 1,
                EffectiveFrom = new(2020, 1, 1), IsActive = true,
                CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
            };
            db.HrClearanceTemplates.Add(template);
            await db.SaveChangesAsync();
            var exitCase = new HrExitCase
            {
                BusinessId = 1, EmployeeId = 1, ExitType = "Resignation",
                InitiatedDate = new(2026, 10, 1), ProposedLastWorkingDate = new(2026, 10, 31),
                Reason = "Test", Status = "Approved", InitiatedByUserId = 101,
                InitiatedAtUtc = DateTime.UtcNow, ClearanceTemplateId = template.Id,
                ClearanceTemplateVersionNo = 1, ClearanceTemplateSnapshotJson = "{}", Version = 3
            };
            db.HrExitCases.Add(exitCase);
            await db.SaveChangesAsync();

            var header = new PayrollFinalSettlement
            {
                BusinessId = 1, ExitCaseId = exitCase.Id, EmployeeId = 1,
                CurrentRevisionNo = 1, Version = 1, CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = 101
            };
            var revision = new PayrollFinalSettlementRevision
            {
                BusinessId = 1, RevisionNo = 1, SettlementDate = new(2026, 10, 31),
                CurrencyCode = "BDT", DecimalPrecision = 2, RoundingMode = nameof(MidpointRounding.AwayFromZero),
                GrossDue = 100m, TotalDeductions = 0m, NetSettlement = 100m,
                CalculationInputHash = new('A', 64), SnapshotHash = new('B', 64),
                SnapshotJson = "{}", IdempotencyKey = "postgres-settlement",
                IdempotencyPayloadHash = new('C', 64), CalculatedAtUtc = DateTime.UtcNow,
                CalculatedByUserId = 101
            };
            revision.Lines.Add(new PayrollFinalSettlementLine
            {
                BusinessId = 1, LineNo = 1, ComponentCode = "PAYROLL_NET_DUE",
                ComponentName = "Outstanding approved Payroll net pay",
                LineType = PayrollFinalSettlementLineTypes.Earning, Amount = 100m,
                IsApplicable = true, SourceType = "PayrollEmployeeResult",
                EvidenceSnapshotJson = "{}"
            });
            header.Revisions.Add(revision);
            db.PayrollFinalSettlements.Add(header);
            await db.SaveChangesAsync();

            await AssertBlockedAsync(db,
                $"UPDATE payroll_final_settlement_revisions SET snapshot_json = 'changed' WHERE id = {revision.Id}");
            await AssertBlockedAsync(db,
                $"DELETE FROM payroll_final_settlement_revisions WHERE id = {revision.Id}");
            await AssertBlockedAsync(db,
                $"UPDATE payroll_final_settlement_lines SET amount = 0 WHERE id = {revision.Lines.Single().Id}");
            await AssertBlockedAsync(db,
                $"DELETE FROM payroll_final_settlement_lines WHERE id = {revision.Lines.Single().Id}");
            await AssertBlockedAsync(db,
                $"DELETE FROM payroll_final_settlements WHERE id = {header.Id}");

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE payroll_final_settlements SET current_revision_no = 2, version = 2 WHERE id = {header.Id}");
            await AssertBlockedAsync(db,
                $"UPDATE payroll_final_settlements SET version = 3 WHERE id = {header.Id}");
        });
    }

    private static RetailPOSDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connectionString).Options);

    private static async Task AssertBlockedAsync(RetailPOSDbContext db, string sql)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
    }
}
