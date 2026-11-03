namespace RetailPOS.API.Configuration;

public sealed class DatabaseStartupOptions
{
    public bool MigrateOnStartup { get; init; }
    public bool SeedOnStartup { get; init; }
}

public static class RedisCacheRegistration
{
    public const string DefaultInstanceName = "pos:";

    /// <summary>
    /// Registers IDistributedCache once: Redis when ConnectionStrings:Redis is configured, otherwise the
    /// in-memory fallback (Development/CI only; strict environments are rejected by
    /// <see cref="InfrastructureConfigurationValidator"/>). PosCache:InstanceName isolates the key namespace.
    /// </summary>
    public static IServiceCollection AddPosDistributedCache(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConnectionString))
            return services.AddDistributedMemoryCache();

        var instanceName = configuration["PosCache:InstanceName"];
        return services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = string.IsNullOrWhiteSpace(instanceName) ? DefaultInstanceName : instanceName;
        });
    }
}
