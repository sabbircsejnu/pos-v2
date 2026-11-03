using Microsoft.Extensions.Configuration;
using Npgsql;

namespace RetailPOS.Tests.Infrastructure;

/// <summary>
/// Resolves isolated integration-test infrastructure settings from environment variables, then the test
/// project's user-secrets. Nothing here ever points at Development resources.
/// </summary>
internal static class InfrastructureTestSettings
{
    public const string PostgresKey = "RETAILPOS_TEST_POSTGRES";
    public const string RabbitMqUriKey = "RETAILPOS_TEST_RABBITMQ_URI";
    public const string RedisKey = "RETAILPOS_TEST_REDIS";
    public const string ElasticsearchUriKey = "RETAILPOS_TEST_ELASTICSEARCH_URI";
    public const string ElasticsearchUserKey = "RETAILPOS_TEST_ELASTICSEARCH_USER";
    public const string ElasticsearchPasswordKey = "RETAILPOS_TEST_ELASTICSEARCH_PASSWORD";

    public const string RabbitMqTestVirtualHost = "retailpos_test";
    public const string RedisTestKeyPrefix = "retailpos-test:";
    public const string ElasticsearchTestIndexPrefix = "retailpos-test-";
    public const int DevelopmentPostgresPort = 5435;
    public const string DevelopmentDatabasePrefix = "retailpos_db";

    private static readonly Lazy<IConfiguration> Configuration = new(() =>
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(InfrastructureTestSettings).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build());

    public static string? Get(string key) =>
        string.IsNullOrWhiteSpace(Configuration.Value[key]) ? null : Configuration.Value[key];

    public static string Require(string key) =>
        Get(key) ?? throw new InvalidOperationException(
            $"{key} is not configured. Set it as an environment variable or in the RetailPOS.IntegrationTests user-secrets (see Docs/Development/LOCAL_INFRASTRUCTURE.md).");

    /// <summary>
    /// Promotes RETAILPOS_TEST_POSTGRES from user-secrets to the process environment (existing fixtures read it
    /// from there) and refuses to run when it targets the persistent Development PostgreSQL port.
    /// </summary>
    internal static void ConfigureIsolatedPostgres()
    {
        var configured = Get(PostgresKey);
        if (configured is null)
            return;

        AssertNotDevelopmentPostgres(configured);
        Environment.SetEnvironmentVariable(PostgresKey, configured);
    }

    /// <summary>
    /// Returns the explicitly configured isolated Test PostgreSQL connection. There is deliberately no fallback:
    /// an unset value fails the test instead of silently reaching the persistent Development server.
    /// </summary>
    internal static string RequireIsolatedPostgres()
    {
        var configured = Require(PostgresKey);
        AssertNotDevelopmentPostgres(configured);
        return configured;
    }

    internal static void AssertNotDevelopmentPostgres(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Port == DevelopmentPostgresPort)
            throw new InvalidOperationException(
                $"{PostgresKey} targets the persistent Development PostgreSQL port {DevelopmentPostgresPort}; integration tests must use the dedicated A2 instance.");

        if (builder.Database is { Length: > 0 } database
            && database.StartsWith(DevelopmentDatabasePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{PostgresKey} targets the persistent application database '{database}'; integration tests must use the dedicated A2 instance.");
    }
}
