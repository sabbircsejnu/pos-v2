using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Infrastructure;

/// <summary>
/// The three database strategies on the isolated Test PostgreSQL: the migrated template is complete, immutable and
/// protected; clones are equivalent to a real migration and isolated from each other; FreshMigration and Lifecycle stay
/// empty; template invalidation and interrupted-build recovery work without touching anything that is not theirs.
/// </summary>
public sealed class PostgresTestTemplateTests
{
    [Fact]
    public async Task The_template_is_ready_immutable_and_carries_the_complete_migration_history()
    {
        var connection = InfrastructureTestSettings.RequireIsolatedPostgres();
        var template = await PostgresTestTemplate.EnsureAsync(connection);

        Assert.Equal(PostgresTestTemplate.CurrentName, template.Name);
        await using (var admin = await OpenAdminAsync(connection))
        {
            var state = await PostgresTestTemplate.ReadAsync(admin, template.Name);
            Assert.NotNull(state);
            Assert.True(state!.IsTemplate);
            Assert.False(state.AllowConnections);
            Assert.StartsWith("retailpos-test-template:ready:", state.Marker);
        }

        await PostgresTestDatabase.RunAsync(connection, PostgresTestDatabasePrefix.SaleCreation, async database =>
        {
            Assert.Equal(PostgresTestDatabaseStrategy.TemplateClone, database.Strategy);
            await using var db = Context(database.ConnectionString);
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            // The existing fixtures still call MigrateAsync(); on a clone it is a no-op.
            await db.Database.MigrateAsync();
            Assert.True(await ScalarAsync<bool>(database.ConnectionString,
                "SELECT count(*) = 5 FROM pg_trigger WHERE tgname IN ('trg_sale_financial_postings_guard','trg_sales_require_open_cash_session','trg_cash_session_movements_require_open','trg_cash_session_movements_append_only','trg_cash_session_close_snapshots_immutable')"));
        });
    }

    [Fact]
    public async Task A_clone_has_exactly_the_schema_of_a_real_fresh_migration()
    {
        var connection = InfrastructureTestSettings.RequireIsolatedPostgres();
        string? cloneSchema = null;
        await PostgresTestDatabase.RunAsync(connection, PostgresTestDatabasePrefix.MigrationBaseline, async clone =>
            cloneSchema = await SchemaFingerprintAsync(clone.ConnectionString));

        string? freshSchema = null;
        await PostgresTestDatabase.RunAsync(connection, PostgresTestDatabasePrefix.MigrationBaseline,
            PostgresTestDatabaseStrategy.FreshMigration, async fresh =>
            {
                await using (var db = Context(fresh.ConnectionString))
                    await db.Database.MigrateAsync();
                freshSchema = await SchemaFingerprintAsync(fresh.ConnectionString);
            });

        Assert.False(string.IsNullOrEmpty(cloneSchema));
        Assert.Equal(freshSchema, cloneSchema);
    }

    [Fact]
    public async Task Parallel_clones_are_isolated_from_each_other()
    {
        var connection = InfrastructureTestSettings.RequireIsolatedPostgres();
        await using var first = await PostgresTestDatabase.CreateAsync(connection, PostgresTestDatabasePrefix.SaleCreation);
        await using var second = await PostgresTestDatabase.CreateAsync(connection, PostgresTestDatabasePrefix.SaleCreation);
        Assert.NotEqual(first.Name, second.Name);

        await Task.WhenAll(
            ExecuteAsync(first.ConnectionString, "CREATE TABLE isolation_probe (owner text); INSERT INTO isolation_probe VALUES ('first')"),
            ExecuteAsync(second.ConnectionString, "CREATE TABLE isolation_probe (owner text); INSERT INTO isolation_probe VALUES ('second')"));

        Assert.Equal("first", await ScalarAsync<string>(first.ConnectionString, "SELECT string_agg(owner, ',') FROM isolation_probe"));
        Assert.Equal("second", await ScalarAsync<string>(second.ConnectionString, "SELECT string_agg(owner, ',') FROM isolation_probe"));

        // A new clone starts from the unmodified template, without either fixture's changes.
        await PostgresTestDatabase.RunAsync(connection, PostgresTestDatabasePrefix.SaleCreation, async third =>
            Assert.False(await ScalarAsync<bool>(third.ConnectionString, "SELECT to_regclass('public.isolation_probe') IS NOT NULL")));
    }

    [Theory]
    [InlineData(nameof(PostgresTestDatabaseStrategy.FreshMigration))]
    [InlineData(nameof(PostgresTestDatabaseStrategy.Lifecycle))]
    public async Task FreshMigration_and_Lifecycle_databases_are_empty(string strategyName)
    {
        var strategy = Enum.Parse<PostgresTestDatabaseStrategy>(strategyName);
        await PostgresTestDatabase.RunAsync(InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.MigrationBaseline, strategy, async database =>
            {
                Assert.Equal(strategy, database.Strategy);
                Assert.Equal(0L, await ScalarAsync<long>(database.ConnectionString,
                    "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'"));
            });
    }

    [Fact]
    public void The_fingerprint_and_name_change_whenever_the_migration_script_changes()
    {
        var original = PostgresTestTemplate.ComputeFingerprint("CREATE TABLE a (id int);");
        var changed = PostgresTestTemplate.ComputeFingerprint("CREATE TABLE a (id bigint);");

        Assert.Equal(original, PostgresTestTemplate.ComputeFingerprint("CREATE TABLE a (id int);"));
        Assert.NotEqual(original, changed);
        Assert.NotEqual(PostgresTestTemplate.NameFor(original), PostgresTestTemplate.NameFor(changed));
        Assert.True(PostgresTestTemplate.NameFor(original).Length <= 63);
    }

    [Fact]
    public async Task Cleanup_can_never_drop_the_template()
    {
        var connection = InfrastructureTestSettings.RequireIsolatedPostgres();
        var template = await PostgresTestTemplate.EnsureAsync(connection);

        Assert.False(PostgresTestDatabase.IsApprovedDatabaseName(template.Name));
        var forged = new PostgresTestDatabaseCandidate(
            template.Name, 1, $"retailpos-test-owner:{Guid.NewGuid():N}:1:{Guid.NewGuid():N}", "admin", 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresTestDatabase.DropOrphansAsync(connection, [forged]));
        Assert.DoesNotContain(await PostgresTestDatabase.InventoryOrphansAsync(connection), candidate => candidate.Name == template.Name);
        await using var admin = await OpenAdminAsync(connection);
        Assert.NotNull(await PostgresTestTemplate.ReadAsync(admin, template.Name));
    }

    [Fact]
    public async Task An_interrupted_build_is_rebuilt_and_a_foreign_database_under_the_name_is_refused()
    {
        var connection = InfrastructureTestSettings.RequireIsolatedPostgres();
        var fingerprint = PostgresTestTemplate.ComputeFingerprint($"recovery-test-{Guid.NewGuid():N}");
        var name = PostgresTestTemplate.NameFor(fingerprint);
        var builds = 0;
        Task Build(string templateConnection, CancellationToken _)
        {
            Interlocked.Increment(ref builds);
            return ExecuteAsync(templateConnection, "CREATE TABLE recovery_marker (id int)");
        }

        await using var admin = await OpenAdminAsync(connection);
        try
        {
            // Interrupted build: the database exists with this fingerprint's "building" marker.
            await ExecuteAsync(admin, $"CREATE DATABASE \"{name}\"");
            await ExecuteAsync(admin, $"COMMENT ON DATABASE \"{name}\" IS 'retailpos-test-template:building:{fingerprint}'");

            var rebuilt = await PostgresTestTemplate.EnsureAsync(connection, name, fingerprint, Build);
            Assert.True(rebuilt.Built);
            Assert.Equal(1, builds);
            var state = await PostgresTestTemplate.ReadAsync(admin, name);
            Assert.Equal($"retailpos-test-template:ready:{fingerprint}", state!.Marker);
            Assert.True(state.IsTemplate);
            Assert.False(state.AllowConnections);

            // Same process, same fingerprint: built once.
            Assert.Same(rebuilt, await PostgresTestTemplate.EnsureAsync(connection, name, fingerprint, Build));
            Assert.Equal(1, builds);

            // A database under a template name without this template's marker is never replaced.
            await PostgresTestTemplate.DropTemplateAsync(admin, name, isTemplate: true);
            await ExecuteAsync(admin, $"CREATE DATABASE \"{name}\"");
            await ExecuteAsync(admin, $"COMMENT ON DATABASE \"{name}\" IS 'someone-else'");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresTestTemplate.EnsureAsync(connection, name, fingerprint + "-x", Build));
            Assert.Equal("someone-else", (await PostgresTestTemplate.ReadAsync(admin, name))!.Marker);
        }
        finally
        {
            var state = await PostgresTestTemplate.ReadAsync(admin, name);
            if (state is not null)
                await PostgresTestTemplate.DropTemplateAsync(admin, name, state.IsTemplate);
        }
    }

    [Fact]
    public async Task Template_operations_refuse_the_Development_server_and_non_template_names()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresTestTemplate.EnsureAsync(
            "Host=127.0.0.1;Port=5435;Database=postgres;Username=u;Password=unused", "retailpos_tpl_x", "f", (_, _) => Task.CompletedTask));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresTestTemplate.EnsureAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(), "retailpos_db", "f", (_, _) => Task.CompletedTask));
    }

    private static RetailPOSDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connectionString).Options);

    private static async Task<NpgsqlConnection> OpenAdminAsync(string connectionString)
    {
        var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false }.ConnectionString);
        await admin.OpenAsync();
        return admin;
    }

    /// <summary>Tables/columns, indexes, constraints, triggers, functions and migration history, as one canonical text.</summary>
    private static Task<string> SchemaFingerprintAsync(string connectionString) => ScalarAsync<string>(connectionString, """
        SELECT md5(string_agg(item, E'\n' ORDER BY item)) FROM (
            SELECT 'col:' || table_name || '.' || column_name || ':' || data_type || ':' || is_nullable || ':' || COALESCE(column_default, '') AS item
              FROM information_schema.columns WHERE table_schema = 'public'
            UNION ALL SELECT 'idx:' || indexname || ':' || indexdef FROM pg_indexes WHERE schemaname = 'public'
            UNION ALL SELECT 'con:' || c.conrelid::regclass || ':' || c.conname || ':' || pg_get_constraintdef(c.oid)
              FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'public'
            UNION ALL SELECT 'trg:' || t.tgrelid::regclass || ':' || t.tgname || ':' || pg_get_triggerdef(t.oid)
              FROM pg_trigger t WHERE NOT t.tgisinternal
            UNION ALL SELECT 'fn:' || p.proname || ':' || md5(pg_get_functiondef(p.oid))
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'public' AND p.prokind = 'f'
            UNION ALL SELECT 'mig:' || "MigrationId" FROM "__EFMigrationsHistory"
        ) schema_items
        """);

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? default! : (T)value!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
