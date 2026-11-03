using Microsoft.Extensions.Configuration;
using Npgsql;

namespace RetailPOS.Tests.Infrastructure;

public sealed class PostgresTestDatabaseLifecycleTests
{
    [Fact]
    public async Task RunAsync_RemovesDatabaseAfterSuccessfulOperation()
    {
        string? name = null;
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.AccountingSequence,
            async database =>
            {
                name = database.Name;
                Assert.True(await ExistsAsync(database.Name));
            });

        Assert.NotNull(name);
        Assert.False(await ExistsAsync(name));
    }

    [Fact]
    public async Task RunAsync_RemovesDatabaseAndPreservesOperationFailure()
    {
        string? name = null;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresTestDatabase.RunAsync(
                ResolveBaseConnectionString(),
                PostgresTestDatabasePrefix.SaleCreation,
                database =>
                {
                    name = database.Name;
                    return Task.FromException(new InvalidOperationException("Expected fixture operation failure."));
                }));

        Assert.Equal("Expected fixture operation failure.", exception.Message);
        Assert.NotNull(name);
        Assert.False(await ExistsAsync(name));
    }

    [Fact]
    public async Task DisposingOneFixture_DoesNotRemoveAnotherFixturesDatabase()
    {
        await using var first = await PostgresTestDatabase.CreateAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.AccountingSequence);
        var second = await PostgresTestDatabase.CreateAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.AccountingSequence);

        try
        {
            await first.DisposeAsync();

            Assert.False(await ExistsAsync(first.Name));
            Assert.True(await ExistsAsync(second.Name));
        }
        finally
        {
            await second.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("retailpos_db")]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("ai_agent_db")]
    [InlineData("iranimart_ecommerce")]
    [InlineData("quickbook_connector")]
    [InlineData("threecx")]
    [InlineData("other_application_db")]
    [InlineData("retailpos_acc_seq_not-a-guid")]
    [InlineData("retailpos_period_close_prepare_not-a-guid")]
    [InlineData("retailpos_unapproved_0123456789abcdef0123456789abcdef")]
    public void CleanupGuard_RejectsProtectedAndUnrecognizedNames(string name) =>
        Assert.False(PostgresTestDatabase.IsApprovedDatabaseName(name));

    [Fact]
    public async Task DisposeAsync_IsSafeWhenDatabaseWasAlreadyRemoved()
    {
        await using var database = await PostgresTestDatabase.CreateAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.MigrationBaseline);
        var adminBuilder = new NpgsqlConnectionStringBuilder(ResolveBaseConnectionString())
        {
            Database = "postgres"
        };

        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE \"{database.Name}\"";
            await drop.ExecuteNonQueryAsync();
        }

        await database.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public void EveryPrefix_PreservesFullGuidWithinPostgresIdentifierLimit()
    {
        var names = Enum.GetValues<PostgresTestDatabasePrefix>()
            .Select(PostgresTestDatabase.CreateName).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(names, name =>
        {
            Assert.True(name.Length <= 63);
            Assert.True(PostgresTestDatabase.IsApprovedDatabaseName(name));
            Assert.True(Guid.TryParseExact(name[^32..], "N", out _));
        });
    }

    [Fact]
    public async Task EveryPrefix_RemovesExactPersistedName()
    {
        foreach (var prefix in Enum.GetValues<PostgresTestDatabasePrefix>())
        {
            var database = await PostgresTestDatabase.CreateAsync(ResolveBaseConnectionString(), prefix);
            await using (database)
            {
                await using var connection = new NpgsqlConnection(database.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT current_database()";
                Assert.Equal(database.Name, await command.ExecuteScalarAsync());
            }
            Assert.False(await ExistsAsync(database.Name));
        }
    }

    [Fact]
    public async Task DisposeAsync_ClearsPoolAndTerminatesOnlyOwnedActiveConnections()
    {
        await using var database = await PostgresTestDatabase.CreateAsync(
            ResolveBaseConnectionString(), PostgresTestDatabasePrefix.AccountingSequence);
        await using var other = await PostgresTestDatabase.CreateAsync(
            ResolveBaseConnectionString(), PostgresTestDatabasePrefix.AccountingSequence);
        await using var active = new NpgsqlConnection(database.ConnectionString);
        await using var otherActive = new NpgsqlConnection(other.ConnectionString);
        await active.OpenAsync();
        await otherActive.OpenAsync();
        await using (var pooled = new NpgsqlConnection(database.ConnectionString))
            await pooled.OpenAsync();

        await Task.WhenAll(database.DisposeAsync().AsTask(), database.DisposeAsync().AsTask());
        Assert.False(await ExistsAsync(database.Name));
        Assert.True(await ExistsAsync(other.Name));
        await using var command = otherActive.CreateCommand();
        command.CommandText = "SELECT 1";
        Assert.Equal(1, await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("retailpos_db")]
    [InlineData("iranimart_ecommerce")]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("retailpos_unapproved_0123456789abcdef0123456789abcdef")]
    public async Task OrphanCleanup_RejectsUnregisteredProtectedOrUnapprovedNames(string name)
    {
        var forged = new PostgresTestDatabaseCandidate(
            name, 1, $"retailpos-test-owner:{Guid.NewGuid():N}:1:{Guid.NewGuid():N}", "admin", 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresTestDatabase.DropOrphansAsync(ResolveBaseConnectionString(), [forged]));
        Assert.False(PostgresTestDatabase.IsApprovedDatabaseName(name));
    }

    [Fact]
    public async Task InitializationFailure_DisposesResourceAndPreservesOriginalFailure()
    {
        var resource = new RecordingResource();
        var failure = new InvalidOperationException("Expected initialization failure.");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresTestDatabase.InitializeResourceAsync(resource, _ => Task.FromException(failure)));
        Assert.Same(failure, actual);
        Assert.True(resource.Disposed);
    }

    [Fact]
    public async Task InitializationAndCleanupFailure_ReportsBothErrors()
    {
        var resource = new RecordingResource { FailDisposal = true };
        var failure = new InvalidOperationException("Expected initialization failure.");
        var actual = await Assert.ThrowsAsync<AggregateException>(() =>
            PostgresTestDatabase.InitializeResourceAsync(resource, _ => Task.FromException(failure)));
        Assert.Same(failure, actual.InnerExceptions[0]);
        Assert.Equal("Expected disposal failure.", actual.InnerExceptions[1].Message);
    }

    private sealed class RecordingResource : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public bool FailDisposal { get; init; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (FailDisposal)
                throw new InvalidOperationException("Expected disposal failure.");
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<bool> ExistsAsync(string name)
    {
        var builder = new NpgsqlConnectionStringBuilder(ResolveBaseConnectionString())
        {
            Database = "postgres"
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)";
        command.Parameters.AddWithValue("name", name);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static string ResolveBaseConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured for PostgreSQL integration tests.");
    }
}
