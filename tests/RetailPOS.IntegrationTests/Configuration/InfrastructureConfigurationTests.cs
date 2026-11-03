using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RetailPOS.API.Configuration;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Configuration;

public sealed class InfrastructureConfigurationTests
{
    private static readonly string ApiRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API"));

    private const string StagingPostgres =
        "Host=staging-db.example.invalid;Port=5432;Database=retailpos_staging;Username=app;Password=dummy-value;SSL Mode=Require";
    private const string StagingRedis = "staging-redis.example.invalid:6380,password=dummy-value,ssl=true";

    private static readonly string DummyJwtSecret = new('j', 48);

    private static IConfigurationRoot Load(string environment, params (string Key, string? Value)[] overrides)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ApiRoot, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(ApiRoot, $"appsettings.{environment}.json"), optional: true);
        builder.AddInMemoryCollection(overrides.Select(o => new KeyValuePair<string, string?>(o.Key, o.Value)));
        return builder.Build();
    }

    private static (string Key, string? Value)[] Injected(params (string Key, string? Value)[] extra) =>
        new (string Key, string? Value)[]
        {
            ("ConnectionStrings:DefaultConnection", StagingPostgres),
            ("ConnectionStrings:Redis", StagingRedis),
            ("JwtSettings:Secret", DummyJwtSecret)
        }.Concat(extra).ToArray();

    [Fact]
    public void SharedAndEnvironmentFiles_ContainNoCredentialsOrDevelopmentFallbacks()
    {
        foreach (var file in new[] { "appsettings.json", "appsettings.Development.json",
                     "appsettings.Staging.json", "appsettings.Production.json" })
        {
            var text = File.ReadAllText(Path.Combine(ApiRoot, file));
            Assert.DoesNotContain("password=", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"Secret\"", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("YourSuperSecretKey", text, StringComparison.Ordinal);
            if (file != "appsettings.Development.json")
            {
                Assert.DoesNotContain("127.0.0.1", text, StringComparison.Ordinal);
                Assert.DoesNotContain("localhost", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("5435", text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Development_BindsExistingLocalEndpointsAndIsValidWithoutSecrets()
    {
        var config = Load("Development");

        Assert.Contains("127.0.0.1", config["ConnectionStrings:DefaultConnection"]);
        Assert.Contains("Port=5435", config["ConnectionStrings:DefaultConnection"]);
        Assert.StartsWith("127.0.0.1:6382", config["ConnectionStrings:Redis"]);
        Assert.Equal("pos:", config["PosCache:InstanceName"]);
        Assert.Equal("False", config["DurableDelivery:Enabled"]);
        Assert.Equal("False", config["Database:MigrateOnStartup"]);
        Assert.Equal("http://127.0.0.1:9200", config["Elasticsearch:Uri"]);
        Assert.Empty(InfrastructureConfigurationValidator.Validate(config, "Development"));
    }

    [Theory]
    [InlineData("Staging", "pos-staging:", "retailpos-staging")]
    [InlineData("Production", "pos-prod:", "retailpos-prod")]
    public void StagingAndProduction_WithInjectedDummyConfiguration_AreValid(
        string environment, string instanceName, string indexPrefix)
    {
        var config = Load(environment, Injected());

        Assert.Equal(instanceName, config["PosCache:InstanceName"]);
        Assert.Equal(indexPrefix, config["Elasticsearch:IndexPrefix"]);
        Assert.Equal("False", config["DurableDelivery:Enabled"]);
        Assert.Equal("True", config["FeatureFlags:StockCountPhase3Enabled"]);
        Assert.Empty(InfrastructureConfigurationValidator.Validate(config, environment));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void StagingAndProduction_WithoutInjectedSettings_FailWithSanitizedKeyOnlyErrors(string environment)
    {
        var config = Load(environment);

        var exception = Assert.Throws<InvalidOperationException>(
            () => InfrastructureConfigurationValidator.ValidateOrThrow(config, environment));

        Assert.Contains("ConnectionStrings:DefaultConnection is required", exception.Message);
        Assert.Contains("ConnectionStrings:Redis is required", exception.Message);
        Assert.DoesNotContain("127.0.0.1", exception.Message);
        Assert.DoesNotContain("5435", exception.Message);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void StrictEnvironments_RejectLocalDevelopmentEndpointsAndDevelopmentCacheNamespace(string environment)
    {
        var config = Load(environment,
            ("ConnectionStrings:DefaultConnection", "Host=127.0.0.1;Port=5435;Database=retailpos_db;Username=admin;Password=dummy-value"),
            ("ConnectionStrings:Redis", "127.0.0.1:6382,password=dummy-value"),
            ("PosCache:InstanceName", "pos:"),
            ("JwtSettings:Secret", DummyJwtSecret));

        var errors = InfrastructureConfigurationValidator.Validate(config, environment);

        Assert.Contains(errors, e => e.StartsWith("ConnectionStrings:DefaultConnection must not target a local"));
        Assert.Contains(errors, e => e.StartsWith("ConnectionStrings:DefaultConnection must set SSL Mode"));
        Assert.Contains(errors, e => e.StartsWith("ConnectionStrings:Redis must not target a local"));
        Assert.Contains(errors, e => e.StartsWith("ConnectionStrings:Redis must enable TLS"));
        Assert.Contains(errors, e => e.StartsWith("PosCache:InstanceName"));
        Assert.All(errors, e => Assert.DoesNotContain("dummy-value", e));
    }

    [Theory]
    [InlineData("Staging", "Database:MigrateOnStartup")]
    [InlineData("Production", "Database:SeedOnStartup")]
    public void StrictEnvironments_RejectAutomaticMigrationAndSeeding(string environment, string key)
    {
        var errors = InfrastructureConfigurationValidator.Validate(
            Load(environment, Injected((key, "true"))), environment);

        Assert.Contains(errors, e => e.StartsWith(key) && e.Contains("must be false"));
    }

    [Fact]
    public void StrictEnvironments_RequireExplicitWorkerAndDurableDeliveryFlags()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = StagingPostgres,
            ["ConnectionStrings:Redis"] = StagingRedis,
            ["PosCache:InstanceName"] = "pos-prod:",
            ["Database:MigrateOnStartup"] = "false",
            ["Database:SeedOnStartup"] = "false",
            ["JwtSettings:Secret"] = DummyJwtSecret
        }).Build();

        var errors = InfrastructureConfigurationValidator.Validate(config, "Production");

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("DurableDelivery:Enabled must be set explicitly"));
        Assert.Contains(errors, e => e.StartsWith("MobileStockTake:Submission:WorkerEnabled must be set explicitly"));
    }

    [Fact]
    public void InMemoryCacheFallback_IsAllowedInDevelopmentButNotInProduction()
    {
        Assert.Empty(InfrastructureConfigurationValidator.Validate(
            Load("Development", ("ConnectionStrings:Redis", "")), "Development"));

        var errors = InfrastructureConfigurationValidator.Validate(
            Load("Production", ("ConnectionStrings:DefaultConnection", StagingPostgres),
                ("JwtSettings:Secret", DummyJwtSecret)), "Production");
        Assert.Contains(errors, e => e.StartsWith("ConnectionStrings:Redis is required"));
    }

    [Fact]
    public void DurableDelivery_WhenEnabled_RequiresTlsDedicatedVirtualHostAndNonLocalBroker()
    {
        string[] Validate(string uri) => InfrastructureConfigurationValidator.Validate(
            Load("Production", Injected(("DurableDelivery:Enabled", "true"), ("DurableDelivery:ConnectionUri", uri))),
            "Production").ToArray();

        Assert.Contains(Validate("amqp://user:dummy@broker.example.invalid:5672/prod"),
            e => e.StartsWith("DurableDelivery:ConnectionUri/Exchange"));
        Assert.Contains(Validate("amqps://user:dummy@broker.example.invalid:5671/"),
            e => e.Contains("dedicated, environment-specific virtual host"));
        Assert.Contains(Validate("amqps://user:dummy@127.0.0.1:5671/prod"),
            e => e.Contains("local Development/Test broker"));
        Assert.Empty(Validate("amqps://user:dummy@broker.example.invalid:5671/retailpos_prod"));
    }

    [Fact]
    public void Elasticsearch_WhenEnabled_RequiresAuthenticationIndexPrefixAndHttpsInStrictEnvironments()
    {
        Assert.Empty(InfrastructureConfigurationValidator.Validate(Load("Production", Injected()), "Production"));

        var weak = Load("Production", Injected(("Elasticsearch:Enabled", "true"),
            ("Elasticsearch:Uri", "http://es.example.invalid:9200"), ("Elasticsearch:IndexPrefix", "")));
        var errors = InfrastructureConfigurationValidator.Validate(weak, "Production");
        Assert.Contains(errors, e => e.Contains("Elasticsearch:Username and Elasticsearch:Password are required"));
        Assert.Contains(errors, e => e.Contains("Elasticsearch:IndexPrefix is required"));
        Assert.Contains(errors, e => e.Contains("must use https"));

        var valid = Load("Production", Injected(("Elasticsearch:Enabled", "true"),
            ("Elasticsearch:Uri", "https://es.example.invalid:9200"),
            ("Elasticsearch:Username", "svc"), ("Elasticsearch:Password", "dummy-value")));
        Assert.Empty(InfrastructureConfigurationValidator.Validate(valid, "Production"));
    }

    [Fact]
    public void IntegrationTestSettings_RefuseTheDevelopmentPostgresPort()
    {
        Assert.Throws<InvalidOperationException>(() => InfrastructureTestSettings.AssertNotDevelopmentPostgres(
            "Host=127.0.0.1;Port=5435;Database=retailpos_db;Username=u;Password=dummy-value"));
        InfrastructureTestSettings.AssertNotDevelopmentPostgres(
            "Host=127.0.0.1;Port=55432;Database=a2;Username=u;Password=dummy-value");
    }

    [Fact]
    public void IntegrationTestSettings_RefuseApplicationDatabaseNames()
    {
        Assert.Throws<InvalidOperationException>(() => InfrastructureTestSettings.AssertNotDevelopmentPostgres(
            "Host=127.0.0.1;Port=55432;Database=retailpos_db_staging;Username=u;Password=unused"));
    }

    [Fact]
    public async Task PostgresTestDatabase_RefusesToCreateDatabasesOnTheDevelopmentServer()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresTestDatabase.CreateAsync(
            "Host=127.0.0.1;Port=5435;Database=postgres;Username=u;Password=unused",
            PostgresTestDatabasePrefix.SaleCreation));
    }

    [Fact]
    public void TestingEnvironment_IsNotSubjectToInfrastructureRules()
    {
        Assert.Empty(InfrastructureConfigurationValidator.Validate(new ConfigurationBuilder().Build(), "Testing"));
    }

    [Theory]
    [InlineData("Development", "pos:", "127.0.0.1:6382")]
    [InlineData("Staging", "pos-staging:", null)]
    [InlineData("Production", "pos-prod:", null)]
    public void HostEnvironmentSelection_LoadsTheMatchingEnvironmentFile(
        string environment, string instanceName, string? redisPrefix)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ContentRootPath = ApiRoot
        });

        Assert.Equal(environment, builder.Environment.EnvironmentName);
        Assert.Equal(instanceName, builder.Configuration["PosCache:InstanceName"]);
        if (redisPrefix is not null)
            Assert.StartsWith(redisPrefix, builder.Configuration["ConnectionStrings:Redis"]);
    }

    [Fact]
    public void ProductionStartup_WithoutDeploymentConfiguration_FailsBeforeServicesStart_AndNeverFallsBackToDevelopment()
    {
        using var baseFactory = new RetailPosApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "",
                    ["ConnectionStrings:Redis"] = ""
                }));
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Infrastructure configuration for environment 'Production' is invalid", exception.Message);
        Assert.DoesNotContain("127.0.0.1", exception.Message);
        Assert.DoesNotContain("5435", exception.Message);
    }

    [Fact]
    public void RedisCacheRegistration_UsesMemoryFallbackOnlyWhenRedisIsNotConfigured()
    {
        using var provider = new ServiceCollection()
            .AddPosDistributedCache(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        Assert.IsNotAssignableFrom<RedisCache>(provider.GetRequiredService<IDistributedCache>());
    }

    [Fact]
    public void RedisCacheRegistration_RegistersASingleRedisCacheWithConfiguredNamespace()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = StagingRedis,
            ["PosCache:InstanceName"] = "pos-staging:"
        }).Build();
        var services = new ServiceCollection().AddPosDistributedCache(config);
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, d => d.ServiceType == typeof(IDistributedCache));
        Assert.IsAssignableFrom<RedisCache>(provider.GetRequiredService<IDistributedCache>());
        Assert.Equal("pos-staging:", provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.InstanceName);
    }

    [Fact]
    public void RedisCacheRegistration_DefaultsToTheExistingDevelopmentNamespace()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = StagingRedis
        }).Build();
        using var provider = new ServiceCollection().AddPosDistributedCache(config).BuildServiceProvider();

        Assert.Equal("pos:", provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.InstanceName);
    }

    [Fact]
    public void TestingHost_RegistersEachBackgroundWorkerOnce_AndNoOutboxWorkerWhileDurableDeliveryIsDisabled()
    {
        using var factory = new RetailPosApiFactory();
        var workers = factory.Services.GetServices<IHostedService>().Select(w => w.GetType()).ToList();

        Assert.Equal(workers.Count, workers.Distinct().Count());
        Assert.DoesNotContain(workers, t => t.Name == "IntegrationOutboxHostedService");
        Assert.DoesNotContain(workers, t => t.Name == "MobileStockTakeSubmissionConsumerHostedService");
    }
}
