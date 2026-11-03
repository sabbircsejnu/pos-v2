using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Subscriptions;

namespace RetailPOS.Tests.Subscriptions;

public sealed class SubscriptionRenewalReminderStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;

    public SubscriptionRenewalReminderStoreTests()
    {
        _connection.Open();
        _db = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ProcessAsyncPreservesInclusiveWindowsAndSuppressesSameDayDuplicates()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var trialAtNow = Business(1, trialEndsAt: now, subscriptionEndsAt: now.AddDays(7));
        var trialAtWindowEnd = Business(2, trialEndsAt: now.AddDays(3));
        var trialExpired = Business(3, trialEndsAt: now.AddTicks(-1));
        var trialOutsideWindow = Business(4, trialEndsAt: now.AddDays(4));
        var inactiveTrial = Business(5, trialEndsAt: now.AddDays(1), isActive: false);
        var subscriptionAtNow = Business(6, subscriptionEndsAt: now);
        var subscriptionAtWindowEnd = Business(7, subscriptionEndsAt: now.AddDays(7));
        var subscriptionOutsideWindow = Business(8, subscriptionEndsAt: now.AddDays(8));
        _db.Businesses.AddRange(
            trialAtNow,
            trialAtWindowEnd,
            trialExpired,
            trialOutsideWindow,
            inactiveTrial,
            subscriptionAtNow,
            subscriptionAtWindowEnd,
            subscriptionOutsideWindow);
        _db.BusinessReminderNotifications.Add(new BusinessReminderNotification
        {
            BusinessId = 2,
            ReminderType = "trial_expiring",
            Message = "already recorded",
            TargetAt = now.AddDays(3).AddHours(2),
            IsDelivered = true
        });
        await _db.SaveChangesAsync();

        var store = new SubscriptionRenewalReminderStore(_db);
        var processed = await store.ProcessAsync(now, CancellationToken.None);

        Assert.Equal(5, processed.Count);
        Assert.Contains(processed, row =>
            row.BusinessId == 1
            && row.ReminderType == "trial_expiring"
            && row.TargetAt == now);
        Assert.Contains(processed, row =>
            row.BusinessId == 2
            && row.ReminderType == "trial_expiring"
            && row.TargetAt == now.AddDays(3));
        Assert.Contains(processed, row =>
            row.BusinessId == 1
            && row.ReminderType == "subscription_expiring"
            && row.TargetAt == now.AddDays(7));
        Assert.DoesNotContain(processed, row => row.BusinessId is 3 or 4 or 5 or 8);

        var persisted = await _db.BusinessReminderNotifications
            .OrderBy(row => row.BusinessId)
            .ThenBy(row => row.ReminderType)
            .ToListAsync();
        Assert.Equal(5, persisted.Count);
        Assert.Single(persisted, row =>
            row.BusinessId == 2 && row.ReminderType == "trial_expiring");
        Assert.Contains(persisted, row =>
            row.BusinessId == 1
            && row.ReminderType == "trial_expiring"
            && row.Message == "Trial expires on 2026-10-01."
            && row.TargetAt == now
            && row.IsDelivered);
        Assert.Contains(persisted, row =>
            row.BusinessId == 7
            && row.ReminderType == "subscription_expiring"
            && row.Message == "Subscription expires on 2026-10-08."
            && row.TargetAt == now.AddDays(7)
            && row.IsDelivered);

        await store.ProcessAsync(now, CancellationToken.None);
        Assert.Equal(5, await _db.BusinessReminderNotifications.CountAsync());
    }

    private static Business Business(
        long id,
        DateTime? trialEndsAt = null,
        DateTime? subscriptionEndsAt = null,
        bool isActive = true) =>
        new()
        {
            Id = id,
            Name = $"Business {id}",
            Email = $"business{id}@example.test",
            TrialEndsAt = trialEndsAt,
            SubscriptionEndsAt = subscriptionEndsAt,
            IsActive = isActive
        };
}
