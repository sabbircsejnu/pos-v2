using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.FeatureEntitlements;

namespace RetailPOS.IntegrationTests.FeatureEntitlements;

public sealed class FeatureEntitlementReadStoreTests : IAsyncLifetime
{
    private readonly string _connectionString =
        $"Data Source=feature-entitlement-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True";
    private SqliteConnection _anchorConnection = null!;

    public async Task InitializeAsync()
    {
        _anchorConnection = new SqliteConnection(_connectionString);
        await _anchorConnection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        context.Businesses.AddRange(
            new Business { Id = 1, Name = "Business one" },
            new Business { Id = 2, Name = "Business two" });
        context.BusinessFeatureSettings.Add(new BusinessFeatureSetting
        {
            BusinessId = 1,
            FeatureKey = "sales.refund",
            IsEnabled = false
        });
        context.BusinessFeatureSettings.Add(new BusinessFeatureSetting
        {
            BusinessId = 2,
            FeatureKey = "sales.refund",
            IsEnabled = true
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task IsEnabledAsync_PreservesDisabledAndMissingRowDefaultBehavior()
    {
        await using var context = CreateContext();
        var store = new FeatureEntitlementReadStore(context);

        var disabled = await store.IsEnabledAsync(1, "sales.refund", CancellationToken.None);
        var missing = await store.IsEnabledAsync(1, "sales.hold", CancellationToken.None);
        var otherBusiness = await store.IsEnabledAsync(2, "sales.refund", CancellationToken.None);

        Assert.False(disabled);
        Assert.True(missing);
        Assert.True(otherBusiness);
    }

    public async Task DisposeAsync()
    {
        await _anchorConnection.DisposeAsync();
    }

    private RetailPOSDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connectionString)
            .Options);
}
