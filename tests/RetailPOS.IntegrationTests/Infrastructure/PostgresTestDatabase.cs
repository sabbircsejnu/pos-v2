using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Npgsql;

namespace RetailPOS.Tests.Infrastructure;

internal enum PostgresTestDatabasePrefix
{
    AccountingSequence, SaleCreation, MigrationBaseline, PurchaseReceive, GrnCompletion,
    ExpenseApproval, OtherIncomeApproval, OpeningBalancePost, SettlementPosting,
    ReconciliationMatch, StatementAdjustmentReversal, PeriodClosePrepare,
    FinancialYearClosePrepare, StockAdjustmentApproval, StockAdjustmentReverse,
    StockTransferDispatch, StockRequisitionConversion, StockRequisitionLifecycle,
    PricingRuleRead, StockCountCreate, StockCountAdjustmentGeneration, PayrollAdjustment,
    InventoryLocationAuthorization, MobileSalesDashboard, OwnerDashboard
}

internal sealed record PostgresTestDatabaseCandidate(
    string Name, long Oid, string Marker, string Owner, long Sessions, bool IsInvalid = false);

internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private const string OwnershipCommentPrefix = "retailpos-test-owner";
    private const int IdentifierLimit = 63;
    private const int MaxConcurrentFreshMigrationDatabases = 2;
    private const string MaxCloneDatabasesKey = "RETAILPOS_TEST_MAX_CLONE_DATABASES";
    private const int DefaultMaxCloneDatabases = 4;
    private const int MaxConcurrentLifecycleDatabases = 4;
    // Each strategy has its own slots. Full-schema migrations stay bounded at 2 to avoid exhausting PostgreSQL's shared
    // lock table; clones apply no migrations, and lifecycle databases are empty, so they no longer queue behind them.
    private static readonly IReadOnlyDictionary<PostgresTestDatabaseStrategy, SemaphoreSlim> DatabaseSlots =
        new Dictionary<PostgresTestDatabaseStrategy, SemaphoreSlim>
        {
            [PostgresTestDatabaseStrategy.FreshMigration] = new(MaxConcurrentFreshMigrationDatabases, MaxConcurrentFreshMigrationDatabases),
            [PostgresTestDatabaseStrategy.TemplateClone] = new(MaxCloneDatabases(), MaxCloneDatabases()),
            [PostgresTestDatabaseStrategy.Lifecycle] = new(MaxConcurrentLifecycleDatabases, MaxConcurrentLifecycleDatabases)
        };
    private static readonly ConcurrentDictionary<PostgresTestDatabaseStrategy, (int Count, long Milliseconds)> CreationStats = new();
    private static readonly string CurrentRunId =
        Guid.TryParseExact(Environment.GetEnvironmentVariable("RETAILPOS_TEST_RUN_ID"), "N", out var runId)
            ? runId.ToString("N") : Guid.NewGuid().ToString("N");
    private static readonly ConcurrentDictionary<string, string> ActiveDatabases = new(StringComparer.Ordinal);
    private readonly string _adminConnectionString;
    private readonly string _ownershipComment;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private readonly SemaphoreSlim _slot;
    private bool _disposed;
    private int _databaseSlotReleased;

    static PostgresTestDatabase() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Console.WriteLine($"RETAILPOS_TEST_DB_STATS|{DescribeCreationStats()}");

    private PostgresTestDatabase(
        string name, string connectionString, string adminConnectionString, string marker,
        PostgresTestDatabaseStrategy strategy, SemaphoreSlim slot)
    {
        Name = name;
        ConnectionString = connectionString;
        _adminConnectionString = adminConnectionString;
        _ownershipComment = marker;
        Strategy = strategy;
        _slot = slot;
    }

    public string Name { get; }
    public string ConnectionString { get; }
    public PostgresTestDatabaseStrategy Strategy { get; }

    private static int MaxCloneDatabases() =>
        int.TryParse(Environment.GetEnvironmentVariable(MaxCloneDatabasesKey), out var configured) && configured is > 0 and <= 32
            ? configured
            : DefaultMaxCloneDatabases;

    /// <summary>Per-strategy count and total creation time (template build excluded) for this test process.</summary>
    internal static string DescribeCreationStats() =>
        string.Join("|", CreationStats.OrderBy(entry => entry.Key).Select(entry =>
            $"{entry.Key}:count={entry.Value.Count},totalMs={entry.Value.Milliseconds},avgMs={(entry.Value.Count == 0 ? 0 : entry.Value.Milliseconds / entry.Value.Count)}"));

    internal static string CreateName(PostgresTestDatabasePrefix prefix)
    {
        var approvedPrefix = GetPrefix(prefix);
        // Preserve the full GUID: PostgreSQL silently truncates identifiers beyond 63 bytes.
        var boundedPrefix = approvedPrefix[..Math.Min(approvedPrefix.Length, IdentifierLimit - 33)];
        return $"{boundedPrefix}_{Guid.NewGuid():N}";
    }

    /// <summary>Creates a clone of the shared migrated template (<see cref="PostgresTestDatabaseStrategy.TemplateClone"/>).</summary>
    public static Task<PostgresTestDatabase> CreateAsync(
        string baseConnectionString, PostgresTestDatabasePrefix prefix,
        CancellationToken cancellationToken = default) =>
        CreateAsync(baseConnectionString, prefix, PostgresTestDatabaseStrategy.TemplateClone, cancellationToken);

    public static async Task<PostgresTestDatabase> CreateAsync(
        string baseConnectionString, PostgresTestDatabasePrefix prefix, PostgresTestDatabaseStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        // Single choke point: every PostgreSQL-backed test creates its database here, so no caller can reach Development.
        InfrastructureTestSettings.AssertNotDevelopmentPostgres(baseConnectionString);
        // The template is built (or found ready) before a slot is taken, so a first-time build never holds a slot.
        var template = strategy == PostgresTestDatabaseStrategy.TemplateClone
            ? (await PostgresTestTemplate.EnsureAsync(baseConnectionString, cancellationToken)).Name
            : null;
        var slot = DatabaseSlots[strategy];
        await slot.WaitAsync(cancellationToken);
        var slotTransferred = false;
        try
        {
            var name = CreateName(prefix);
            if (!IsApprovedDatabaseName(name))
                throw new InvalidOperationException($"Generated PostgreSQL test database name {name} failed its guard.");

            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            { Database = "postgres", Pooling = false };
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = name };
            var marker = $"{OwnershipCommentPrefix}:{CurrentRunId}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:{Guid.NewGuid():N}";
            var database = new PostgresTestDatabase(name, databaseBuilder.ConnectionString,
                adminBuilder.ConnectionString, marker, strategy, slot);
            await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync(cancellationToken);
            var stopwatch = Stopwatch.StartNew();
            await using (var create = admin.CreateCommand())
            {
                // The template refuses connections, so nothing can hold it open while PostgreSQL copies it.
                create.CommandText = template is null
                    ? $"CREATE DATABASE \"{name}\""
                    : $"CREATE DATABASE \"{name}\" TEMPLATE \"{template}\"";
                await create.ExecuteNonQueryAsync(cancellationToken);
            }
            var elapsed = stopwatch.ElapsedMilliseconds;
            CreationStats.AddOrUpdate(strategy, (1, elapsed), (_, current) => (current.Count + 1, current.Milliseconds + elapsed));

            if (!ActiveDatabases.TryAdd(name, marker))
                throw new InvalidOperationException($"PostgreSQL test database {name} is already registered.");
            try
            {
                await using var mark = admin.CreateCommand();
                mark.CommandText = $"COMMENT ON DATABASE \"{name}\" IS '{marker}'";
                await mark.ExecuteNonQueryAsync(cancellationToken);
                slotTransferred = true;
                return database;
            }
            catch (Exception creationException)
            {
                try
                {
                    // Creation is already registered; setup cancellation must not cancel its cleanup.
                    await using var cleanupAdmin = new NpgsqlConnection(adminBuilder.ConnectionString);
                    await cleanupAdmin.OpenAsync();
                    await database.DropAsync(cleanupAdmin);
                    ActiveDatabases.TryRemove(name, out _);
                }
                catch (Exception cleanupException)
                {
                    Console.Error.WriteLine($"PostgreSQL test database setup cleanup failed for {name}: {cleanupException}");
                    throw new AggregateException($"Setup and cleanup failed for {name}.", creationException, cleanupException);
                }
                throw;
            }
        }
        finally
        {
            if (!slotTransferred)
                slot.Release();
        }
    }

    public static Task RunAsync(
        string baseConnectionString, PostgresTestDatabasePrefix prefix,
        Func<PostgresTestDatabase, Task> operation) =>
        RunAsync(baseConnectionString, prefix, PostgresTestDatabaseStrategy.TemplateClone, operation);

    public static async Task RunAsync(
        string baseConnectionString, PostgresTestDatabasePrefix prefix, PostgresTestDatabaseStrategy strategy,
        Func<PostgresTestDatabase, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var database = await CreateAsync(baseConnectionString, prefix, strategy);
        Exception? operationException = null;
        try
        {
            // Callers dispose contexts, scopes and connections before this delegate returns.
            await operation(database);
        }
        catch (Exception exception)
        {
            operationException = exception;
        }
        finally
        {
            try
            {
                await database.DisposeAsync();
            }
            catch (Exception cleanupException)
            {
                if (operationException is not null)
                    throw new AggregateException($"Test operation and cleanup of {database.Name} both failed.",
                        operationException, cleanupException);
                throw;
            }
        }
        if (operationException is not null)
            ExceptionDispatchInfo.Capture(operationException).Throw();
    }

    internal static async Task<T> InitializeResourceAsync<T>(T resource, Func<T, Task> initialize)
        where T : IAsyncDisposable
    {
        try
        {
            await initialize(resource);
            return resource;
        }
        catch (Exception setupException)
        {
            try
            {
                await resource.DisposeAsync();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Test resource initialization and disposal both failed.",
                    setupException, cleanupException);
            }
            throw;
        }
    }

    internal static bool IsApprovedDatabaseName(string name) =>
        Enum.GetValues<PostgresTestDatabasePrefix>().Any(prefix =>
        {
            var fullPrefix = GetPrefix(prefix);
            var boundedPrefix = fullPrefix[..Math.Min(fullPrefix.Length, IdentifierLimit - 33)];
            return MatchesGuid(name, fullPrefix) || MatchesGuid(name, boundedPrefix);
        });

    private static bool MatchesGuid(string name, string prefix) =>
        name.Length <= IdentifierLimit
        && name.StartsWith(prefix + "_", StringComparison.Ordinal)
        && Guid.TryParseExact(name[(prefix.Length + 1)..], "N", out _);

    private static bool IsLegacyTruncatedName(string name) =>
        name.Length == IdentifierLimit && Enum.GetValues<PostgresTestDatabasePrefix>().Any(prefix =>
        {
            var value = GetPrefix(prefix) + "_";
            return value.Length + 32 > IdentifierLimit
                && name.StartsWith(value, StringComparison.Ordinal)
                && name[value.Length..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
        });

    private static bool IsTestMarker(string marker)
    {
        var parts = marker.Split(':');
        return parts.Length == 4 && parts[0] == OwnershipCommentPrefix
            && Guid.TryParseExact(parts[1], "N", out _)
            && long.TryParse(parts[2], out var createdAt)
            && createdAt > 0 && createdAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            && Guid.TryParseExact(parts[3], "N", out _);
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeLock.WaitAsync();
        try
        {
            if (_disposed)
                return;
            if (!IsApprovedDatabaseName(Name)
                || !ActiveDatabases.TryGetValue(Name, out var marker)
                || marker != _ownershipComment)
                throw new InvalidOperationException($"Refusing cleanup of {Name}: not owned by this lifecycle.");

            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            var candidate = await ReadCandidateAsync(admin, Name);
            if (candidate is not null)
            {
                if (candidate.Marker != _ownershipComment)
                    throw new InvalidOperationException($"Refusing cleanup of {Name}: its ownership marker changed.");
                await DropAsync(admin, candidate.IsInvalid);
            }
            ActiveDatabases.TryRemove(Name, out _);
            _disposed = true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PostgreSQL test database cleanup failed for {Name}: {exception}");
            throw;
        }
        finally
        {
            _disposeLock.Release();
            if (Interlocked.Exchange(ref _databaseSlotReleased, 1) == 0)
                _slot.Release();
        }
    }

    private async Task DropAsync(NpgsqlConnection admin, bool isInvalid = false)
    {
        // Clear only this database's pool; never disturb other parallel fixtures.
        using var pooledConnection = new NpgsqlConnection(ConnectionString);
        NpgsqlConnection.ClearPool(pooledConnection);
        await DropExactAsync(admin, Name, isInvalid);
    }

    private static async Task DropExactAsync(NpgsqlConnection admin, string name, bool isInvalid = false)
    {
        // Defence in depth: the shared template is never a disposable database, whatever the caller passes.
        if (PostgresTestTemplate.IsTemplateName(name) || !(IsApprovedDatabaseName(name) || IsLegacyTruncatedName(name)))
            throw new InvalidOperationException($"Refusing to drop {name}: not a disposable test database.");
        if (isInvalid)
        {
            // An interrupted DROP leaves datconnlimit=-2; PostgreSQL forbids ALTER on that database.
            await using var dropInvalid = admin.CreateCommand();
            dropInvalid.CommandText = $"DROP DATABASE IF EXISTS \"{name}\"";
            await dropInvalid.ExecuteNonQueryAsync();
            return;
        }
        await using (var disable = admin.CreateCommand())
        {
            disable.CommandText = $"ALTER DATABASE \"{name}\" WITH ALLOW_CONNECTIONS false";
            await disable.ExecuteNonQueryAsync();
        }
        try
        {
            await using (var terminate = admin.CreateCommand())
            {
                terminate.CommandText = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()";
                terminate.Parameters.AddWithValue("name", name);
                await terminate.ExecuteNonQueryAsync();
            }
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\"";
            await drop.ExecuteNonQueryAsync();
        }
        catch (Exception cleanupException)
        {
            try
            {
                await using var restore = admin.CreateCommand();
                restore.CommandText = $"ALTER DATABASE \"{name}\" WITH ALLOW_CONNECTIONS true";
                await restore.ExecuteNonQueryAsync();
            }
            catch (Exception restoreException)
            {
                throw new AggregateException($"Drop and connection-state restoration failed for {name}.",
                    cleanupException, restoreException);
            }
            throw;
        }
    }

    // This operation is explicit and bounded; normal fixture creation never sweeps other databases.
    internal static async Task<IReadOnlyList<PostgresTestDatabaseCandidate>> InventoryOrphansAsync(string baseConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = """
            SELECT datname, oid::bigint, COALESCE(shobj_description(oid,'pg_database'),''),
                   pg_get_userbyid(datdba), (SELECT count(*) FROM pg_stat_activity a WHERE a.datname=d.datname),
                   datconnlimit = -2
            FROM pg_database d
            WHERE NOT datistemplate AND pg_get_userbyid(datdba)=current_user
            ORDER BY datname
            """;
        var result = new List<PostgresTestDatabaseCandidate>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var candidate = new PostgresTestDatabaseCandidate(reader.GetString(0), reader.GetInt64(1),
                reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetBoolean(5));
            if (IsOrphanCandidate(candidate))
                result.Add(candidate);
        }
        return result;
    }

    private static bool IsOrphanCandidate(PostgresTestDatabaseCandidate candidate) =>
        (IsApprovedDatabaseName(candidate.Name) || IsLegacyTruncatedName(candidate.Name))
        && IsTestMarker(candidate.Marker) && candidate.Marker.Split(':')[1] != CurrentRunId
        && !ActiveDatabases.ContainsKey(candidate.Name) && candidate.Sessions == 0;

    internal static async Task DropOrphansAsync(
        string baseConnectionString, IReadOnlyList<PostgresTestDatabaseCandidate> approvedCandidates)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false };
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        foreach (var approved in approvedCandidates)
        {
            if (!IsOrphanCandidate(approved))
                throw new InvalidOperationException($"Refusing orphan cleanup of {approved.Name}: not an approved marked orphan.");
            var current = await ReadCandidateAsync(admin, approved.Name);
            if (current is null)
                continue;
            if (current != approved || !IsOrphanCandidate(current) || current.Owner != builder.Username)
                throw new InvalidOperationException($"Refusing orphan cleanup of {approved.Name}: ownership/session evidence changed.");
            try
            {
                await DropExactAsync(admin, approved.Name, current.IsInvalid);
                Console.WriteLine($"REMOVED_RETAILPOS_TEST_DATABASE|{approved.Name}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Orphan cleanup failed for {approved.Name}: {exception}");
                throw;
            }
        }
    }

    private static async Task<PostgresTestDatabaseCandidate?> ReadCandidateAsync(NpgsqlConnection admin, string name)
    {
        await using var command = admin.CreateCommand();
        command.CommandText = """
            SELECT datname, oid::bigint, COALESCE(shobj_description(oid,'pg_database'),''),
                   pg_get_userbyid(datdba), (SELECT count(*) FROM pg_stat_activity a WHERE a.datname=d.datname),
                   datconnlimit = -2
            FROM pg_database d WHERE datname=@name
            """;
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetBoolean(5))
            : null;
    }

    private static string GetPrefix(PostgresTestDatabasePrefix prefix) => prefix switch
    {
        PostgresTestDatabasePrefix.AccountingSequence => "retailpos_acc_seq",
        PostgresTestDatabasePrefix.SaleCreation => "retailpos_sale_create",
        PostgresTestDatabasePrefix.MigrationBaseline => "retailpos_baseline",
        PostgresTestDatabasePrefix.PurchaseReceive => "retailpos_purchase_receive",
        PostgresTestDatabasePrefix.GrnCompletion => "retailpos_grn_complete",
        PostgresTestDatabasePrefix.ExpenseApproval => "retailpos_expense_approval",
        PostgresTestDatabasePrefix.OtherIncomeApproval => "retailpos_other_income_approval",
        PostgresTestDatabasePrefix.OpeningBalancePost => "retailpos_opening_balance",
        PostgresTestDatabasePrefix.SettlementPosting => "retailpos_settlement_posting",
        PostgresTestDatabasePrefix.ReconciliationMatch => "retailpos_recon_match",
        PostgresTestDatabasePrefix.StatementAdjustmentReversal => "retailpos_adjustment_reversal",
        PostgresTestDatabasePrefix.PeriodClosePrepare => "retailpos_period_close_prepare",
        PostgresTestDatabasePrefix.FinancialYearClosePrepare => "retailpos_financial_year_close_prepare",
        PostgresTestDatabasePrefix.StockAdjustmentApproval => "retailpos_stock_adjustment_approval",
        PostgresTestDatabasePrefix.StockAdjustmentReverse => "retailpos_stock_adjustment_reverse",
        PostgresTestDatabasePrefix.StockTransferDispatch => "retailpos_stock_transfer_dispatch",
        PostgresTestDatabasePrefix.StockRequisitionConversion => "retailpos_stock_requisition_conversion",
        PostgresTestDatabasePrefix.StockRequisitionLifecycle => "retailpos_stock_requisition_lifecycle",
        PostgresTestDatabasePrefix.PricingRuleRead => "retailpos_pricing_rule_read",
        PostgresTestDatabasePrefix.StockCountCreate => "retailpos_stock_count_create",
        PostgresTestDatabasePrefix.StockCountAdjustmentGeneration => "retailpos_stock_count_adjustment_generation",
        PostgresTestDatabasePrefix.PayrollAdjustment => "retailpos_payroll_adjustment",
        PostgresTestDatabasePrefix.InventoryLocationAuthorization => "retailpos_a2_inventory_location",
        PostgresTestDatabasePrefix.MobileSalesDashboard => "retailpos_mobile_sales_dashboard",
        PostgresTestDatabasePrefix.OwnerDashboard => "retailpos_owner_dashboard",
        _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "Unapproved PostgreSQL test database prefix.")
    };
}
