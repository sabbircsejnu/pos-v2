using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Settings;

namespace RetailPOS.Tests.Settings;

public sealed class BusinessPosSettingsStoreTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly BusinessPosSettingsStore _store;

    public BusinessPosSettingsStoreTests()
    {
        _connection.Open();
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        _db.SaveChanges();

        var legacy = new Mock<ISystemSettingsStore>();
        legacy.Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettings
            {
                Tax = new TaxSettings { TaxEnabled = true, DefaultTaxRate = 7.5m, TaxName = "GST", PricesIncludeTax = true }
            });
        _store = new BusinessPosSettingsStore(_db, legacy.Object);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task BeforeAnySave_TheBusinessKeepsItsLegacyTaxBehaviourAndSafeDefaults()
    {
        var settings = await _store.GetAsync(1, CancellationToken.None);

        Assert.False(settings.IsPersisted);
        Assert.Equal(7.5m, settings.DefaultTaxRate);
        Assert.True(settings.PricesIncludeTax);
        Assert.Equal("GST", settings.TaxName);
        Assert.Equal(20, settings.MaxCashSessionDurationHours);
        Assert.Equal("Asia/Dhaka", settings.DefaultTimeZoneId);
        Assert.Equal(0m, settings.MaxManualDiscountPercent);
    }

    [Fact]
    public async Task Update_PersistsAVersionedAuditableChangeAndLeavesOtherBusinessesAlone()
    {
        var (before, after) = await _store.UpdateAsync(
            1,
            new BusinessPosSettingsUpdate(MaxCashSessionDurationHours: 12, MaxManualDiscountPercent: 15m, PricesIncludeTax: false),
            userId: 42,
            CancellationToken.None);

        Assert.False(before.IsPersisted);
        Assert.True(after.IsPersisted);
        Assert.Equal(1, after.Version);
        Assert.Equal(12, after.MaxCashSessionDurationHours);
        Assert.Equal(15m, after.MaxManualDiscountPercent);
        Assert.False(after.PricesIncludeTax);
        Assert.Equal(7.5m, after.DefaultTaxRate);
        Assert.Equal(42, after.UpdatedByUserId);

        var (second, third) = await _store.UpdateAsync(1, new BusinessPosSettingsUpdate(DefaultTaxRate: 5m), 43, CancellationToken.None);
        Assert.Equal(15m, second.MaxManualDiscountPercent);
        Assert.Equal(2, third.Version);
        Assert.Equal(5m, third.DefaultTaxRate);
        Assert.Equal(15m, third.MaxManualDiscountPercent);

        var other = await _store.GetAsync(2, CancellationToken.None);
        Assert.False(other.IsPersisted);
        Assert.Equal(0m, other.MaxManualDiscountPercent);
        Assert.Equal(20, other.MaxCashSessionDurationHours);
    }

    [Fact]
    public async Task Update_RejectsInvalidValuesAndUnknownBusinesses()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(1, new BusinessPosSettingsUpdate(MaxCashSessionDurationHours: 21), null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(1, new BusinessPosSettingsUpdate(DefaultTimeZoneId: "Nowhere/Land"), null, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _store.UpdateAsync(99, new BusinessPosSettingsUpdate(MaxManualDiscountPercent: 5m), null, CancellationToken.None));
        Assert.Equal(0, await _db.BusinessPosSettings.CountAsync());
    }
}
