using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.BusinessAdministration;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.IntegrationTests.BusinessAdministration;

public sealed class BusinessAdministrationReadStoreTests : IAsyncLifetime
{
    private readonly string _connectionString =
        $"Data Source=business-admin-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True";
    private SqliteConnection _anchorConnection = null!;

    public async Task InitializeAsync()
    {
        _anchorConnection = new SqliteConnection(_connectionString);
        await _anchorConnection.OpenAsync();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        context.Businesses.AddRange(
            new Business
            {
                Id = 1,
                Name = "Zeta Retail",
                Email = "zeta@example.test",
                Phone = "555-0100",
                SubscriptionPlan = "Gold",
                MaxOutlets = 3,
                MaxUsers = 12,
                IsActive = true
            },
            new Business { Id = 2, Name = "Alpha Foods", IsActive = false });
        context.Roles.Add(new Role { Id = 10, Name = "bUsInEsSoWnEr" });
        context.Users.Add(new User
        {
            Id = 11,
            BusinessId = 1,
            RoleId = 10,
            Name = "Owner",
            Email = "owner@example.test"
        });
        context.Outlets.Add(new Outlet { Id = 20, BusinessId = 1, Name = "Main Outlet" });
        context.Warehouses.Add(new Warehouse { Id = 30, BusinessId = 1, Name = "Main Warehouse" });
        context.BusinessFeatureSettings.AddRange(
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "z.feature", IsEnabled = false },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "a.feature", IsEnabled = true });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Queries_PreserveFilteringSummaryAndFeatureSettingContracts()
    {
        await using var context = CreateContext();
        var store = new BusinessAdministrationReadStore(context);

        var businesses = await new GetBusinessesQueryHandler(store).Handle(
            new GetBusinessesQuery(" RETAIL ", true),
            CancellationToken.None);
        var business = Assert.Single(businesses);

        Assert.Equal(1, business.BusinessId);
        Assert.Equal("Zeta Retail", business.BusinessName);
        Assert.Equal("owner@example.test", business.BusinessOwnerEmail);
        Assert.Equal(1, business.UserCount);
        Assert.Equal(1, business.OutletCount);
        Assert.Equal(1, business.WarehouseCount);
        Assert.Equal("Gold", business.SubscriptionPlan);
        Assert.Equal(3, business.MaxOutlets);
        Assert.Equal(12, business.MaxUsers);

        var byId = await new GetBusinessByIdQueryHandler(store).Handle(
            new GetBusinessByIdQuery(1),
            CancellationToken.None);
        Assert.Equal(business, byId);

        var missingBusiness = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new GetBusinessByIdQueryHandler(store).Handle(
                new GetBusinessByIdQuery(999),
                CancellationToken.None));
        Assert.Equal("Business not found.", missingBusiness.Message);

        var features = await new GetBusinessFeatureSettingsQueryHandler(store).Handle(
            new GetBusinessFeatureSettingsQuery(1),
            CancellationToken.None);
        Assert.Equal(new[] { "a.feature", "z.feature" }, features.Select(feature => feature.FeatureKey));
        Assert.False(features[1].IsEnabled);

        var missingFeatures = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new GetBusinessFeatureSettingsQueryHandler(store).Handle(
                new GetBusinessFeatureSettingsQuery(999),
                CancellationToken.None));
        Assert.Equal("Business not found.", missingFeatures.Message);
    }

    [Fact]
    public async Task WriteStore_PersistsStatusAndNormalizedSubscriptionFields()
    {
        await using var context = CreateContext();
        var store = new BusinessAdministrationWriteStore(context);

        Assert.True(await store.SetBusinessActiveAsync(1, false, CancellationToken.None));
        Assert.True(await store.UpdateSubscriptionAsync(
            1,
            "Gold",
            new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            5,
            20,
            CancellationToken.None));
        Assert.False(await store.SetBusinessActiveAsync(999, true, CancellationToken.None));

        var persisted = await context.Businesses.AsNoTracking().SingleAsync(business => business.Id == 1);
        Assert.False(persisted.IsActive);
        Assert.Equal("Gold", persisted.SubscriptionPlan);
        Assert.Equal(5, persisted.MaxOutlets);
        Assert.Equal(20, persisted.MaxUsers);
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), persisted.TrialEndsAt);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), persisted.SubscriptionEndsAt);
    }

    [Fact]
    public async Task WriteStore_UpsertsFeatureSettingsAndLeavesTimestampUntouchedForEmptyInput()
    {
        await using var context = CreateContext();
        var business = await context.Businesses.SingleAsync(item => item.Id == 1);
        business.UpdatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();
        var store = new BusinessAdministrationWriteStore(context);

        Assert.True(await store.UpsertFeatureSettingsAsync(
            1,
            new[]
            {
                new BusinessFeatureSettingChange("a.feature", false, 7),
                new BusinessFeatureSettingChange("a.feature", true, 9),
                new BusinessFeatureSettingChange("new.feature", true, 3),
                new BusinessFeatureSettingChange(string.Empty, false, null)
            },
            CancellationToken.None));

        var settings = await context.BusinessFeatureSettings
            .AsNoTracking()
            .Where(setting => setting.BusinessId == 1)
            .OrderBy(setting => setting.FeatureKey)
            .ToListAsync();
        Assert.Equal(
            new[] { "a.feature", "new.feature", "z.feature" },
            settings.Select(setting => setting.FeatureKey));
        Assert.True(settings[0].IsEnabled);
        Assert.Equal(9, settings[0].LimitValue);
        Assert.Equal(3, settings[1].LimitValue);
        var updatedAt = await context.Businesses
            .AsNoTracking()
            .Where(item => item.Id == 1)
            .Select(item => item.UpdatedAt)
            .SingleAsync();
        Assert.NotEqual(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), updatedAt);

        Assert.True(await store.UpsertFeatureSettingsAsync(
            1,
            Array.Empty<BusinessFeatureSettingChange>(),
            CancellationToken.None));
        var afterEmptyInput = await context.Businesses
            .AsNoTracking()
            .Where(item => item.Id == 1)
            .Select(item => item.UpdatedAt)
            .SingleAsync();
        Assert.Equal(updatedAt, afterEmptyInput);
        Assert.False(await store.UpsertFeatureSettingsAsync(
            999,
            Array.Empty<BusinessFeatureSettingChange>(),
            CancellationToken.None));
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
