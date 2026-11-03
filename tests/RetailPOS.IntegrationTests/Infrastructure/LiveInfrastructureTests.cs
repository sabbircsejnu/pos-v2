using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using RetailPOS.API.Configuration;
using RetailPOS.Application.Integration;
using RetailPOS.Infrastructure.Integration;
using StackExchange.Redis;

namespace RetailPOS.Tests.Infrastructure;

/// <summary>
/// Live, non-destructive checks against the existing local Docker services using ONLY isolated test resources
/// (A2 PostgreSQL, RabbitMQ test vhost, Redis test key namespace, Elasticsearch test indices).
/// Run with: dotnet test --filter "Category=LiveInfrastructure".
/// </summary>
[Trait("Category", "LiveInfrastructure")]
public sealed class LiveInfrastructureTests
{
    [Fact]
    public async Task PostgreSql_DedicatedA2Instance_IsReachableAndIsNotDevelopment()
    {
        var connectionString = InfrastructureTestSettings.Require(InfrastructureTestSettings.PostgresKey);
        InfrastructureTestSettings.AssertNotDevelopmentPostgres(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::int", connection);
        Assert.True((int)(await command.ExecuteScalarAsync())! >= 160000);
        Assert.Equal(55432, new NpgsqlConnectionStringBuilder(connectionString).Port);
    }

    [Fact]
    public async Task RabbitMq_TestVirtualHost_PublishAndConsumeWithAcknowledgement()
    {
        var uri = new Uri(InfrastructureTestSettings.Require(InfrastructureTestSettings.RabbitMqUriKey));
        Assert.Equal(InfrastructureTestSettings.RabbitMqTestVirtualHost, uri.AbsolutePath.TrimStart('/'));

        var suffix = Guid.NewGuid().ToString("N");
        var exchange = $"retailpos-test.{suffix}";
        var queue = $"retailpos-test.{suffix}";
        var payload = "{\"businessId\":1}";
        var envelope = new IntegrationEnvelope(Guid.NewGuid(), 1, "test", "test.changed", 1,
            "test", "42", Guid.NewGuid(), null, DateTime.UtcNow, payload, IntegrationEnvelope.Hash(payload));

        await using var connection = await new ConnectionFactory { Uri = uri }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: false, autoDelete: true);
            await channel.QueueDeclareAsync(queue, durable: false, exclusive: false, autoDelete: true);
            await channel.QueueBindAsync(queue, exchange, envelope.MessageType);

            await RabbitMqConfirmedPublisher.PublishOnConfirmedChannelAsync(channel, exchange, envelope, default);

            var delivery = await channel.BasicGetAsync(queue, autoAck: false);
            Assert.NotNull(delivery);
            Assert.Equal(envelope.MessageId.ToString(), delivery.BasicProperties.MessageId);
            Assert.Equal(DeliveryModes.Persistent, delivery.BasicProperties.DeliveryMode);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);

            Assert.Null(await channel.BasicGetAsync(queue, autoAck: true));
        }
        finally
        {
            await channel.QueueDeleteAsync(queue);
            await channel.ExchangeDeleteAsync(exchange);
        }
    }

    [Fact]
    public async Task RabbitMq_TestCredentials_CannotAccessTheDevelopmentVirtualHost()
    {
        var uri = new Uri(InfrastructureTestSettings.Require(InfrastructureTestSettings.RabbitMqUriKey));
        var developmentUri = new UriBuilder(uri) { Path = "retailpos_dev" }.Uri;

        var factory = new ConnectionFactory { Uri = developmentUri };
        Assert.Equal("retailpos_dev", factory.VirtualHost);

        // The broker closes the connection when the user lacks permissions on the vhost.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
        });
    }

    [Fact]
    public async Task Redis_TestNamespace_SupportsIsolatedKeyOperations_WithoutTouchingOtherKeys()
    {
        var redis = InfrastructureTestSettings.Require(InfrastructureTestSettings.RedisKey);
        var instanceName = $"{InfrastructureTestSettings.RedisTestKeyPrefix}{Guid.NewGuid():N}:";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = redis,
            ["PosCache:InstanceName"] = instanceName
        }).Build();

        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis);
        var database = multiplexer.GetDatabase();
        Assert.True((await database.PingAsync()) < TimeSpan.FromSeconds(3));

        using var provider = new ServiceCollection().AddPosDistributedCache(config).BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        const string key = "lookup:isolation-check";

        await cache.SetStringAsync(key, "value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });
        try
        {
            Assert.Equal("value", await cache.GetStringAsync(key));
            Assert.True(await database.KeyExistsAsync(instanceName + key));
            Assert.False(await database.KeyExistsAsync("pos:" + key));
        }
        finally
        {
            await cache.RemoveAsync(key);
        }

        Assert.False(await database.KeyExistsAsync(instanceName + key));
    }

    [Fact]
    public async Task Elasticsearch_AuthenticationRemainsEnabled_AndTestIndexLifecycleIsIsolated()
    {
        var baseUri = new Uri(InfrastructureTestSettings.Require(InfrastructureTestSettings.ElasticsearchUriKey));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{InfrastructureTestSettings.Require(InfrastructureTestSettings.ElasticsearchUserKey)}:" +
            InfrastructureTestSettings.Require(InfrastructureTestSettings.ElasticsearchPasswordKey)));
        using var anonymous = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };
        using var client = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("_cluster/health")).StatusCode);
        var health = await client.GetAsync("_cluster/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var index = $"{InfrastructureTestSettings.ElasticsearchTestIndexPrefix}{Guid.NewGuid():N}";
        Assert.StartsWith(InfrastructureTestSettings.ElasticsearchTestIndexPrefix, index);
        try
        {
            using var body = new StringContent("{\"sku\":\"TEST-1\"}", Encoding.UTF8, "application/json");
            var indexed = await client.PutAsync($"{index}/_doc/1?refresh=true", body);
            Assert.Equal(HttpStatusCode.Created, indexed.StatusCode);

            var search = await client.GetAsync($"{index}/_search?q=sku:%22TEST-1%22");
            Assert.Equal(HttpStatusCode.OK, search.StatusCode);
            Assert.Contains("\"value\":1", await search.Content.ReadAsStringAsync());
        }
        finally
        {
            Assert.StartsWith(InfrastructureTestSettings.ElasticsearchTestIndexPrefix, index);
            await client.DeleteAsync(index);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(index)).StatusCode);
    }
}
