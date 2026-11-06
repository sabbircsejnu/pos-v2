using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Infrastructure;

/// <summary>
/// How a disposable PostgreSQL test database gets its schema.
/// </summary>
internal enum PostgresTestDatabaseStrategy
{
    /// <summary>Default. A clone of the shared, fully migrated template (schema, triggers, functions, migration history).</summary>
    TemplateClone,

    /// <summary>An empty database; the test builds its schema itself (real EF migrations, Up/Down, or EnsureCreated).</summary>
    FreshMigration,

    /// <summary>An empty database for create/drop/ownership lifecycle tests; no schema is ever applied.</summary>
    Lifecycle
}

/// <summary>
/// The shared migrated template used by <see cref="PostgresTestDatabaseStrategy.TemplateClone"/>. It is built once per
/// schema fingerprint (SHA-256 of the complete EF migration script, which includes every migration's custom SQL), then
/// marked <c>IS_TEMPLATE</c> and <c>ALLOW_CONNECTIONS false</c> so it can never be modified or block a clone. Building is
/// serialised across processes with a PostgreSQL advisory lock; an interrupted build is recognised by its marker and
/// rebuilt. The template name is outside the disposable-database allow-list, so fixture and orphan cleanup cannot drop it.
/// </summary>
internal static class PostgresTestTemplate
{
    internal const string NamePrefix = "retailpos_tpl_";
    private const string MarkerPrefix = "retailpos-test-template";
    // Bump when the template build procedure itself changes (forces a rebuild even if the migrations did not change).
    private const string BuildProcedureVersion = "1";
    private const long AdvisoryLockKey = 0x52504F535450_4C; // "RPOSTPL"

    private static readonly ConcurrentDictionary<string, Lazy<Task<TemplateInfo>>> Templates = new(StringComparer.Ordinal);
    private static readonly Lazy<string> MigrationFingerprint = new(ComputeMigrationFingerprint);

    internal sealed record TemplateInfo(string Name, bool Built, TimeSpan InitializationTime);

    internal static string CurrentName => NameFor(MigrationFingerprint.Value);

    internal static string NameFor(string fingerprint) => NamePrefix + fingerprint[..16];

    internal static bool IsTemplateName(string name) => name.StartsWith(NamePrefix, StringComparison.Ordinal);

    internal static string ComputeFingerprint(string migrationScript) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v{BuildProcedureVersion}\n{migrationScript}"))).ToLowerInvariant();

    /// <summary>Returns the ready template for the current migrations, building it at most once per process and fingerprint.</summary>
    internal static Task<TemplateInfo> EnsureAsync(string baseConnectionString, CancellationToken cancellationToken = default) =>
        EnsureAsync(baseConnectionString, CurrentName, MigrationFingerprint.Value, MigrateAsync, cancellationToken);

    /// <summary>Test seam: a caller-chosen template name, fingerprint and schema builder.</summary>
    internal static async Task<TemplateInfo> EnsureAsync(
        string baseConnectionString, string name, string fingerprint,
        Func<string, CancellationToken, Task> buildSchema, CancellationToken cancellationToken = default)
    {
        InfrastructureTestSettings.AssertNotDevelopmentPostgres(baseConnectionString);
        if (!IsTemplateName(name))
            throw new InvalidOperationException($"{name} is not a test template name.");

        var server = new NpgsqlConnectionStringBuilder(baseConnectionString);
        var key = $"{server.Host}:{server.Port}:{name}:{fingerprint}";
        var lazy = Templates.GetOrAdd(key, _ => new Lazy<Task<TemplateInfo>>(
            () => InitializeAsync(baseConnectionString, name, fingerprint, buildSchema)));
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch when (lazy.Value.IsFaulted)
        {
            // A failed initialization is not cached: the next fixture retries (e.g. after the server came back).
            Templates.TryRemove(new KeyValuePair<string, Lazy<Task<TemplateInfo>>>(key, lazy));
            throw;
        }
    }

    private static async Task<TemplateInfo> InitializeAsync(
        string baseConnectionString, string name, string fingerprint, Func<string, CancellationToken, Task> buildSchema)
    {
        var stopwatch = Stopwatch.StartNew();
        var readyMarker = $"{MarkerPrefix}:ready:{fingerprint}";
        var buildingMarker = $"{MarkerPrefix}:building:{fingerprint}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();

        await ExecuteAsync(admin, $"SELECT pg_advisory_lock({AdvisoryLockKey})");
        try
        {
            var existing = await ReadAsync(admin, name);
            if (existing is { Marker: var marker, IsTemplate: true, AllowConnections: false } && marker == readyMarker)
            {
                Log($"reused|{name}|{stopwatch.ElapsedMilliseconds}ms");
                return new TemplateInfo(name, false, stopwatch.Elapsed);
            }

            if (existing is not null)
            {
                // Only a database carrying this fingerprint's own template marker may be replaced (interrupted build or
                // a ready marker whose flags were changed by hand). Anything else under this name is never touched.
                if (existing.Marker != buildingMarker && existing.Marker != readyMarker)
                    throw new InvalidOperationException(
                        $"Refusing to replace {name}: it does not carry this template's ownership marker.");
                await DropTemplateAsync(admin, name, existing.IsTemplate);
                Log($"recovered|{name}|dropped interrupted or altered template");
            }

            await ExecuteAsync(admin, $"CREATE DATABASE \"{name}\"");
            await ExecuteAsync(admin, $"COMMENT ON DATABASE \"{name}\" IS '{buildingMarker}'");

            var templateConnection = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = name, Pooling = false };
            await buildSchema(templateConnection.ConnectionString, CancellationToken.None);

            await ExecuteAsync(admin, "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()", name);
            await ExecuteAsync(admin, $"ALTER DATABASE \"{name}\" WITH IS_TEMPLATE true ALLOW_CONNECTIONS false");
            await ExecuteAsync(admin, $"COMMENT ON DATABASE \"{name}\" IS '{readyMarker}'");
            Log($"built|{name}|{stopwatch.ElapsedMilliseconds}ms");
            return new TemplateInfo(name, true, stopwatch.Elapsed);
        }
        finally
        {
            await ExecuteAsync(admin, $"SELECT pg_advisory_unlock({AdvisoryLockKey})");
        }
    }

    /// <summary>Drops a test template that this code identified by name and marker. Used only for build recovery and by tests.</summary>
    internal static async Task DropTemplateAsync(NpgsqlConnection admin, string name, bool isTemplate)
    {
        if (!IsTemplateName(name))
            throw new InvalidOperationException($"Refusing to drop {name}: not a test template.");
        if (isTemplate)
            await ExecuteAsync(admin, $"ALTER DATABASE \"{name}\" WITH IS_TEMPLATE false");
        await ExecuteAsync(admin, $"ALTER DATABASE \"{name}\" WITH ALLOW_CONNECTIONS false");
        await ExecuteAsync(admin, "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()", name);
        await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{name}\"");
    }

    internal sealed record TemplateState(string Marker, bool IsTemplate, bool AllowConnections);

    internal static async Task<TemplateState?> ReadAsync(NpgsqlConnection admin, string name)
    {
        await using var command = admin.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(shobj_description(oid, 'pg_database'), ''), datistemplate, datallowconn
            FROM pg_database WHERE datname = @name
            """;
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new TemplateState(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2)) : null;
    }

    private static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var context = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connectionString).Options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private static string ComputeMigrationFingerprint()
    {
        // The full idempotent-free script from an empty database to the latest migration: it changes whenever any
        // migration, its custom SQL or the model snapshot-driven operations change. No database connection is needed.
        using var context = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql("Host=template-fingerprint.invalid;Database=x").Options);
        var script = context.GetService<IMigrator>().GenerateScript(fromMigration: null, toMigration: null);
        return ComputeFingerprint(script);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, string? name = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (name is not null)
            command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync();
    }

    private static void Log(string message) => Console.WriteLine($"RETAILPOS_TEST_TEMPLATE|{message}");
}
