using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Text.Json;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests;

public sealed class MigrationChainValidationTests
{
    [Fact]
    public async Task FreshPostgresDatabase_AppliesPreservedBaselineAndForwardChain()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuredConnection = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configuredConnection))
        {
            var settingsPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "RetailPOS.API", "appsettings.json"));
            configuredConnection = new ConfigurationBuilder()
                .AddJsonFile(settingsPath, optional: false)
                .Build()
                .GetConnectionString("DefaultConnection");
        }

        Assert.False(string.IsNullOrWhiteSpace(configuredConnection));
        var database = await PostgresTestDatabase.CreateAsync(
            configuredConnection,
            PostgresTestDatabasePrefix.MigrationBaseline,
            PostgresTestDatabaseStrategy.FreshMigration);

        try
        {
            var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(database.ConnectionString)
                .Options;

            await using var context = new RetailPOSDbContext(options);
            var expectedMigrations = context.Database.GetMigrations().ToArray();
            Assert.NotEmpty(expectedMigrations);
            Assert.Equal("20261004095137_InitialBaseline", expectedMigrations[0]);
            Assert.Equal("20261108094509_PosV2ExchangeApprovalFailures", expectedMigrations[^1]);
            Assert.All(expectedMigrations.Skip(1), migration =>
                Assert.True(string.CompareOrdinal(migration, expectedMigrations[0]) > 0));
            var employeeMigrationIndex = Array.FindIndex(expectedMigrations,
                migration => migration.EndsWith("_Phase1DEmployeeMaster", StringComparison.Ordinal));
            Assert.True(employeeMigrationIndex > 0);
            await context.Database.MigrateAsync(expectedMigrations[employeeMigrationIndex - 1]);
            // Raw SQL: the current model has columns this older schema does not have yet.
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO businesses (id, name, is_active, created_at, updated_at)
                VALUES (100, 'Existing Business Before Employee Migration', TRUE, now(), now())
                """);
            await context.Database.MigrateAsync();
            Assert.Equal(expectedMigrations, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            await context.Database.MigrateAsync();
            Assert.Equal(expectedMigrations, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            var existingBusinessTypes = await context.HrEmploymentTypes.AsNoTracking()
                .Where(type => type.BusinessId == 100)
                .OrderBy(type => type.Code)
                .Select(type => type.Code)
                .ToArrayAsync();
            Assert.Equal(
                RetailPOS.Application.HR.Employees.HrEmploymentTypeCatalog.Defaults
                    .Select(type => type.Code).OrderBy(code => code),
                existingBusinessTypes);

            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            Assert.Equal(
                "20261004095137_InitialBaseline",
                await ScalarAsync<string>(connection, "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" LIMIT 1"));
            Assert.Equal(expectedMigrations.Length,
                await ScalarAsync<long>(connection, "SELECT count(*) FROM \"__EFMigrationsHistory\""));

            AssertColumn(
                await ReadColumnAsync(connection, "users", "inventory_location_access_scope"),
                false,
                "character varying",
                "'assigned_only'::character varying",
                maxLength: 40);
            AssertColumn(await ReadColumnAsync(connection, "sales", "business_date"), true, "date", null);
            AssertColumn(await ReadColumnAsync(connection, "stock_adjustment_lines", "unit_cost_snapshot"), true, "numeric", null);
            AssertColumn(await ReadColumnAsync(connection, "stock_ledgers", "unit_cost"), false, "numeric", null);
            AssertColumn(await ReadColumnAsync(connection, "stock_ledgers", "movement_value"), false, "numeric", null, precision: 18, scale: 4);
            AssertColumn(await ReadColumnAsync(connection, "stock_ledgers", "business_date"), false, "date", null);
            AssertColumn(await ReadColumnAsync(connection, "stock_ledgers", "movement_sequence"), false, "bigint", null, precision: 64, scale: 0);
            AssertColumn(await ReadColumnAsync(connection, "stock_ledgers", "reversal_of_movement_id"), true, "bigint", null, precision: 64, scale: 0);

            Assert.True(await IndexExistsAsync(connection, "sales", "IX_sales_business_date"));
            Assert.True(await ScalarAsync<bool>(
                connection,
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = 'stock_ledgers'
                      AND indexdef ILIKE '%(variant_id, location_id, location_type, movement_sequence)%'
                )
                """));
            Assert.True(await ScalarAsync<bool>(
                connection,
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = 'cash_sessions'
                      AND indexname = 'ux_cash_sessions_one_open_per_terminal'
                      AND indexdef ILIKE '%UNIQUE%'
                      AND indexdef ILIKE '%WHERE%'
                      AND indexdef ILIKE '%status%'
                      AND indexdef ILIKE '%open%'
                )
                """));
            Assert.True(await ScalarAsync<bool>(
                connection,
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.table_constraints
                    WHERE table_schema = 'public'
                      AND table_name = 'sales'
                      AND constraint_type = 'FOREIGN KEY'
                )
                """));
            Assert.True(await IndexExistsAsync(
                connection, "payroll_runs", "ux_payroll_run_business_offcycle_reference"));
            Assert.True(await IndexExistsAsync(
                connection, "payroll_runs", "ux_payroll_run_business_correction_reference"));
            Assert.True(await IndexExistsAsync(
                connection, "payroll_period_reopen_events", "ux_payroll_period_reopen_event_version"));
            AssertColumn(
                await ReadColumnAsync(connection, "payroll_period_close_assessments", "closed_at_utc"),
                true,
                "timestamp with time zone",
                null);
            Assert.True(await ScalarAsync<bool>(connection,
                "SELECT to_regclass('public.payroll_period_reopen_events') IS NOT NULL"));
            Assert.True(await ScalarAsync<bool>(connection,
                "SELECT to_regclass('public.payroll_accounting_postings') IS NOT NULL"));
            Assert.True(await ScalarAsync<bool>(connection,
                "SELECT to_regclass('public.payroll_accounting_component_mappings') IS NOT NULL"));
            Assert.True(await ScalarAsync<bool>(connection,
                "SELECT to_regclass('public.payroll_final_settlement_approval_submissions') IS NOT NULL"));
            Assert.True(await ScalarAsync<bool>(connection,
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_trigger
                    WHERE tgname = 'trg_payroll_final_settlement_submission_guard'
                      AND NOT tgisinternal
                )
                """));
            Assert.True(await IndexExistsAsync(
                connection, "payroll_accounting_postings", "ux_payroll_accounting_posting_run_revision"));
            Assert.True(await IndexExistsAsync(
                connection, "payroll_accounting_component_mappings",
                "ux_payroll_accounting_component_mapping"));
            Assert.True(await ScalarAsync<bool>(connection,
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = 'payroll_runs'
                      AND indexname = 'ux_payroll_run_period_type_scope'
                      AND indexdef ILIKE '%WHERE%'
                      AND indexdef ILIKE '%run_type%'
                      AND indexdef ILIKE '%Regular%'
                )
                """));

            await DbSeeder.SeedAsync(context);
            await AssertFinalSettlementSubmissionIsImmutableAsync(context);
            var initialSeedCounts = await ReadSeedCountsAsync(context);
            var rolePermissions = await context.Roles.AsNoTracking()
                .ToDictionaryAsync(role => role.Name, role => role.Permissions);
            Assert.Contains("Super Admin", rolePermissions.Keys);
            Assert.Contains("BusinessOwner", rolePermissions.Keys);
            Assert.Contains("Accounting Manager", rolePermissions.Keys);
            Assert.Equal("[\"*\"]", rolePermissions["Super Admin"]);
            Assert.Equal("[\"*\"]", rolePermissions["BusinessOwner"]);
            var accountingPermissions = JsonSerializer.Deserialize<string[]>(rolePermissions["Accounting Manager"]) ?? [];
            Assert.Contains("accounting.reports.vat", accountingPermissions);
            Assert.DoesNotContain("*", accountingPermissions);
            Assert.DoesNotContain("users.create", accountingPermissions);
            Assert.Contains(
                "accounting.chart_accounts.view",
                RetailPOS.Application.Identity.Permissions.PermissionCatalog.All);
            Assert.Contains(
                "accounting.chart_accounts.view",
                RetailPOS.Application.Identity.Permissions.PermissionCatalog.ExpandClaims("accounts.view"));

            await DbSeeder.SeedAsync(context);
            Assert.Equal(initialSeedCounts, await ReadSeedCountsAsync(context));
            Assert.Equal(1, await context.Roles.CountAsync(role => role.Name == "Accounting Manager"));

            context.Businesses.Add(new Business { Id = 101, Name = "Baseline Test Business", IsActive = true });
            context.Outlets.Add(new Outlet { Id = 102, BusinessId = 101, Name = "Baseline Test Outlet" });
            context.Users.Add(new User
            {
                Id = 103,
                BusinessId = 101,
                Name = "Baseline Test User",
                Email = "baseline@example.invalid",
                PasswordHash = "test",
                OutletId = 102,
                IsActive = true
            });
            context.PosTerminals.Add(new PosTerminal
            {
                Id = 104,
                OutletId = 102,
                Name = "Baseline Test Terminal",
                Code = "BASELINE-1",
                IsActive = true
            });
            await context.SaveChangesAsync();

            await context.Database.MigrateAsync();
            Assert.Equal(expectedMigrations, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.True(await context.Businesses.AnyAsync(business => business.Id == 101));
            Assert.True(await context.Users.AnyAsync(user => user.Id == 103));

            context.CashSessions.Add(CreateOpenCashSession(101, 102, 104, 103));
            await context.SaveChangesAsync();
            context.CashSessions.Add(CreateOpenCashSession(101, 102, 104, 103));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();
            Assert.Equal(1, await context.CashSessions.CountAsync(session =>
                session.OutletId == 102 && session.TerminalId == 104 && session.Status == "open"));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static async Task AssertFinalSettlementSubmissionIsImmutableAsync(
        RetailPOSDbContext context)
    {
        const long businessId = 100;
        var employmentType = await context.HrEmploymentTypes
            .Where(row => row.BusinessId == businessId)
            .OrderBy(row => row.Id)
            .FirstAsync();
        var employee = new HrEmployee
        {
            BusinessId = businessId,
            EmployeeCode = "FINAL-SETTLEMENT-TRIGGER",
            FirstName = "Trigger",
            DisplayName = "Final Settlement Trigger Test",
            CurrentEmploymentTypeId = employmentType.Id,
            JoiningDate = new DateOnly(2024, 1, 1)
        };
        context.HrEmployees.Add(employee);

        var template = new HrClearanceTemplate
        {
            BusinessId = businessId,
            Code = "FINAL-SETTLEMENT-TRIGGER",
            Name = "Final settlement trigger test",
            ScopeType = HrClearanceTemplateScopeType.Business,
            VersionNo = 1,
            EffectiveFrom = new DateOnly(2024, 1, 1),
            IsActive = true,
            CreatedByUserId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.HrClearanceTemplates.Add(template);
        await context.SaveChangesAsync();

        var exitCase = new HrExitCase
        {
            BusinessId = businessId,
            EmployeeId = employee.Id,
            ExitType = "Resignation",
            InitiatedDate = new DateOnly(2026, 10, 1),
            ProposedLastWorkingDate = new DateOnly(2026, 10, 31),
            ActualLastWorkingDate = new DateOnly(2026, 10, 31),
            Reason = "PostgreSQL immutability trigger test",
            Status = "Approved",
            InitiatedByUserId = 1,
            InitiatedAtUtc = DateTime.UtcNow,
            ClearanceTemplateId = template.Id,
            ClearanceTemplateVersionNo = template.VersionNo,
            ClearanceTemplateSnapshotJson = "{}"
        };
        context.HrExitCases.Add(exitCase);
        await context.SaveChangesAsync();

        var settlement = new PayrollFinalSettlement
        {
            BusinessId = businessId,
            ExitCaseId = exitCase.Id,
            EmployeeId = employee.Id,
            CurrentRevisionNo = 1,
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 1
        };
        context.PayrollFinalSettlements.Add(settlement);
        await context.SaveChangesAsync();

        var revision = new PayrollFinalSettlementRevision
        {
            BusinessId = businessId,
            FinalSettlementId = settlement.Id,
            RevisionNo = 1,
            SettlementDate = new DateOnly(2026, 10, 31),
            CurrencyCode = "BDT",
            DecimalPrecision = 2,
            RoundingMode = MidpointRounding.AwayFromZero.ToString(),
            GrossDue = 100,
            TotalDeductions = 0,
            NetSettlement = 100,
            CalculationInputHash = new string('A', 64),
            SnapshotHash = new string('B', 64),
            SnapshotJson = "{}",
            IdempotencyKey = "trigger-test-revision",
            IdempotencyPayloadHash = new string('C', 64),
            CalculatedAtUtc = DateTime.UtcNow,
            CalculatedByUserId = 1
        };
        context.PayrollFinalSettlementRevisions.Add(revision);
        await context.SaveChangesAsync();

        const string originalPayloadHash = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";
        var submission = new PayrollFinalSettlementApprovalSubmission
        {
            BusinessId = businessId,
            FinalSettlementId = settlement.Id,
            RevisionId = revision.Id,
            RevisionNo = revision.RevisionNo,
            EmployeeId = employee.Id,
            ExitCaseId = exitCase.Id,
            CalculationSnapshotHash = revision.SnapshotHash,
            ComponentLinesHash = new string('E', 64),
            GrossPayable = 100,
            TotalRecovery = 0,
            NetSettlement = 100,
            CurrencyCode = revision.CurrencyCode,
            SourceEvidenceHash = revision.CalculationInputHash,
            CalculationPolicyVersion = "Phase7A-v1",
            ClearanceReferencesJson = "[]",
            ClearanceReferencesHash = new string('F', 64),
            SubmitterUserId = 1,
            SubmittedAtUtc = DateTime.UtcNow,
            IdempotencyKey = "trigger-test-submission",
            PayloadHash = originalPayloadHash,
            SubmittedEvidenceJson = "{}",
            SubmittedEvidenceHash = new string('A', 64)
        };
        context.PayrollFinalSettlementApprovalSubmissions.Add(submission);
        await context.SaveChangesAsync();

        submission.PayloadHash = new string('B', 64);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        var persisted = await context.PayrollFinalSettlementApprovalSubmissions.SingleAsync();
        Assert.Equal(originalPayloadHash, persisted.PayloadHash);

        context.PayrollFinalSettlementApprovalSubmissions.Remove(persisted);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        Assert.Single(await context.PayrollFinalSettlementApprovalSubmissions.ToArrayAsync());
    }

    private static CashSession CreateOpenCashSession(long businessId, long outletId, long terminalId, long userId)
    {
        var now = DateTime.UtcNow;
        return new CashSession
        {
            BusinessId = businessId,
            OutletId = outletId,
            TerminalId = terminalId,
            BusinessDate = now.Date,
            OpenedAt = now,
            OpeningCashAmount = 100m,
            Status = "open",
            OpenedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static async Task<bool> IndexExistsAsync(NpgsqlConnection connection, string table, string index) =>
        await ScalarAsync<bool>(
            connection,
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND tablename = @table AND indexname = @index)",
            ("table", table),
            ("index", index));

    private static async Task<(int Roles, int Users, int Outlets, int Terminals, int Categories, int Products, int Variants, int Warehouses, int Customers, int Suppliers, int Transactions)> ReadSeedCountsAsync(
        RetailPOSDbContext context) =>
        (
            await context.Roles.CountAsync(),
            await context.Users.CountAsync(),
            await context.Outlets.CountAsync(),
            await context.PosTerminals.CountAsync(),
            await context.Categories.CountAsync(),
            await context.Products.CountAsync(),
            await context.ProductVariants.CountAsync(),
            await context.Warehouses.CountAsync(),
            await context.Customers.CountAsync(),
            await context.Suppliers.CountAsync(),
            await context.Transactions.CountAsync());

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(string DataType, bool Nullable, string? Default, int? Precision, int? Scale, int? MaxLength)> ReadColumnAsync(
        NpgsqlConnection connection,
        string table,
        string column)
    {
        await using var command = new NpgsqlCommand(
            "SELECT data_type, is_nullable = 'YES', column_default, numeric_precision, numeric_scale, character_maximum_length " +
            "FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table AND column_name = @column",
            connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Expected {table}.{column} to exist.");
        return (
            reader.GetString(0),
            reader.GetBoolean(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5));
    }

    private static void AssertColumn(
        (string DataType, bool Nullable, string? Default, int? Precision, int? Scale, int? MaxLength) actual,
        bool nullable,
        string dataType,
        string? defaultValue,
        int? maxLength = null,
        int? precision = null,
        int? scale = null)
    {
        Assert.Equal(dataType, actual.DataType);
        Assert.Equal(nullable, actual.Nullable);
        Assert.Equal(defaultValue, actual.Default);
        Assert.Equal(maxLength, actual.MaxLength);
        Assert.Equal(precision, actual.Precision);
        Assert.Equal(scale, actual.Scale);
    }
}
