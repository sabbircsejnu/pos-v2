using System.Net;
using Npgsql;
using RetailPOS.Infrastructure.Integration;
using StackExchange.Redis;

namespace RetailPOS.API.Configuration;

/// <summary>
/// Environment-aware validation of infrastructure settings (PostgreSQL, Redis, RabbitMQ, Elasticsearch,
/// database startup policy). Messages only ever contain configuration key names, never values.
/// Development is lenient and Testing is skipped; every other environment (Staging, Production, custom)
/// is strict and never accepts local Development endpoints.
/// </summary>
public static class InfrastructureConfigurationValidator
{
    private const string TestingEnvironment = "Testing";
    private static readonly int[] LocalPostgresPorts = [5435, 55432];

    public static bool IsStrictEnvironment(string environmentName) =>
        !string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(environmentName, TestingEnvironment, StringComparison.OrdinalIgnoreCase);

    public static void ValidateOrThrow(IConfiguration configuration, string environmentName)
    {
        var errors = Validate(configuration, environmentName);
        if (errors.Count == 0)
            return;

        throw new InvalidOperationException(
            $"Infrastructure configuration for environment '{environmentName}' is invalid:{Environment.NewLine}"
            + string.Join(Environment.NewLine, errors.Select(e => " - " + e))
            + $"{Environment.NewLine}Provide the missing values through environment variables or the deployment secret store.");
    }

    public static IReadOnlyList<string> Validate(IConfiguration configuration, string environmentName)
    {
        var errors = new List<string>();
        if (string.Equals(environmentName, TestingEnvironment, StringComparison.OrdinalIgnoreCase))
            return errors;

        var strict = IsStrictEnvironment(environmentName);
        ValidatePostgres(configuration, strict, errors);
        ValidateDatabaseStartupPolicy(configuration, strict, errors);
        ValidateRedis(configuration, strict, errors);
        ValidateDurableDelivery(configuration, strict, errors);
        ValidateElasticsearch(configuration, strict, errors);
        if (strict)
            ValidateJwtSecret(configuration, errors);
        return errors;
    }

    private static void ValidatePostgres(IConfiguration configuration, bool strict, List<string> errors)
    {
        const string key = "ConnectionStrings:DefaultConnection";
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is required.");
            return;
        }

        NpgsqlConnectionStringBuilder builder;
        try { builder = new NpgsqlConnectionStringBuilder(value); }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            errors.Add($"{key} is not a valid PostgreSQL connection string.");
            return;
        }

        if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database)
            || string.IsNullOrWhiteSpace(builder.Username))
        {
            errors.Add($"{key} must specify Host, Database and Username.");
            return;
        }

        if (!strict)
            return;

        if (builder.Host.Split(',').Any(IsLocalHost) || LocalPostgresPorts.Contains(builder.Port))
            errors.Add($"{key} must not target a local Development/Test endpoint.");
        if (string.IsNullOrWhiteSpace(builder.Password))
            errors.Add($"{key} must include a Password supplied by the deployment secret store.");
        if (builder.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
            errors.Add($"{key} must set SSL Mode to Require, VerifyCA or VerifyFull.");
    }

    private static void ValidateDatabaseStartupPolicy(IConfiguration configuration, bool strict, List<string> errors)
    {
        if (!strict)
            return;

        foreach (var key in new[] { "Database:MigrateOnStartup", "Database:SeedOnStartup" })
        {
            if (!bool.TryParse(configuration[key], out var enabled))
                errors.Add($"{key} must be set explicitly to false; apply migrations through the controlled release pipeline.");
            else if (enabled)
                errors.Add($"{key} must be false; automatic migration/seeding is not allowed outside Development.");
        }
    }

    private static void ValidateRedis(IConfiguration configuration, bool strict, List<string> errors)
    {
        const string key = "ConnectionStrings:Redis";
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            if (strict)
                errors.Add($"{key} is required; the in-memory cache fallback is only permitted in Development.");
            return;
        }

        ConfigurationOptions options;
        try { options = ConfigurationOptions.Parse(value); }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            errors.Add($"{key} is not a valid Redis configuration string.");
            return;
        }

        if (options.EndPoints.Count == 0)
            errors.Add($"{key} must specify at least one endpoint.");
        if (!strict)
            return;

        if (options.EndPoints.Any(IsLocalEndpoint))
            errors.Add($"{key} must not target a local Development/Test endpoint.");
        if (string.IsNullOrWhiteSpace(options.Password))
            errors.Add($"{key} must include a password supplied by the deployment secret store.");
        if (!options.Ssl)
            errors.Add($"{key} must enable TLS (ssl=true).");

        var instanceName = configuration["PosCache:InstanceName"];
        if (string.IsNullOrWhiteSpace(instanceName)
            || string.Equals(instanceName, RedisCacheRegistration.DefaultInstanceName, StringComparison.Ordinal))
            errors.Add("PosCache:InstanceName must be set to an environment-specific cache namespace (not the Development default).");
    }

    private static void ValidateDurableDelivery(IConfiguration configuration, bool strict, List<string> errors)
    {
        if (strict && !bool.TryParse(configuration["DurableDelivery:Enabled"], out _))
            errors.Add("DurableDelivery:Enabled must be set explicitly to true or false.");
        if (strict && !bool.TryParse(configuration["MobileStockTake:Submission:WorkerEnabled"], out _))
            errors.Add("MobileStockTake:Submission:WorkerEnabled must be set explicitly to true or false.");

        var options = configuration.GetSection("DurableDelivery").Get<RabbitMqDeliveryOptions>();
        if (options is not { Enabled: true })
            return;

        try { options.Validate(); }
        catch (InvalidOperationException)
        {
            errors.Add("DurableDelivery:ConnectionUri/Exchange/PublishTimeoutSeconds are invalid (an amqps:// URI with credentials and an exchange are required).");
            return;
        }

        if (!strict)
            return;

        var uri = new Uri(options.ConnectionUri!);
        if (IsLocalHost(uri.Host))
            errors.Add("DurableDelivery:ConnectionUri must not target a local Development/Test broker.");
        var virtualHost = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (string.IsNullOrWhiteSpace(virtualHost) || virtualHost == "/")
            errors.Add("DurableDelivery:ConnectionUri must use a dedicated, environment-specific virtual host (not the default '/').");
    }

    private static void ValidateElasticsearch(IConfiguration configuration, bool strict, List<string> errors)
    {
        var section = configuration.GetSection("Elasticsearch");
        if (!section.GetValue("Enabled", false))
            return;

        var uriValid = Uri.TryCreate(section["Uri"], UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
        if (!uriValid)
            errors.Add("Elasticsearch:Uri must be an absolute http(s) URI without embedded credentials.");
        if (string.IsNullOrWhiteSpace(section["Username"]) || string.IsNullOrWhiteSpace(section["Password"]))
            errors.Add("Elasticsearch:Username and Elasticsearch:Password are required; anonymous access is not supported.");
        if (string.IsNullOrWhiteSpace(section["IndexPrefix"]))
            errors.Add("Elasticsearch:IndexPrefix is required to isolate this environment's indices.");
        if (!strict || !uriValid)
            return;

        if (IsLocalHost(uri!.Host))
            errors.Add("Elasticsearch:Uri must not target a local Development/Test endpoint.");
        if (uri.Scheme != Uri.UriSchemeHttps)
            errors.Add("Elasticsearch:Uri must use https.");
    }

    private static void ValidateJwtSecret(IConfiguration configuration, List<string> errors)
    {
        var secret = configuration["JwtSettings:Secret"];
        if (!string.IsNullOrWhiteSpace(secret)
            && (secret.Length < 32 || secret.StartsWith("YourSuperSecretKey", StringComparison.Ordinal)))
            errors.Add("JwtSettings:Secret must be a unique deployment secret of at least 32 characters.");
    }

    private static bool IsLocalEndpoint(EndPoint endpoint) => endpoint switch
    {
        DnsEndPoint dns => IsLocalHost(dns.Host),
        IPEndPoint ip => IsLocalAddress(ip.Address),
        _ => false
    };

    private static bool IsLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;
        host = host.Trim();
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(host.Trim('[', ']'), out var address) && IsLocalAddress(address);
    }

    private static bool IsLocalAddress(IPAddress address) =>
        IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
}
