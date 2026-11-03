using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Subscriptions.Abstractions;
using RetailPOS.Application.Subscriptions.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Subscriptions;

public sealed class SubscriptionRenewalReminderStore(
    RetailPOSDbContext dbContext)
    : ISubscriptionRenewalReminderStore
{
    public async Task<IReadOnlyList<SubscriptionRenewalReminderReadModel>> ProcessAsync(
        DateTime currentTimeUtc,
        CancellationToken cancellationToken)
    {
        var trialReminderWindow = currentTimeUtc.AddDays(3);
        var subscriptionReminderWindow = currentTimeUtc.AddDays(7);
        var trialExpiring = await dbContext.Businesses
            .AsNoTracking()
            .Where(business => business.IsActive
                && business.TrialEndsAt.HasValue
                && business.TrialEndsAt.Value >= currentTimeUtc
                && business.TrialEndsAt.Value <= trialReminderWindow)
            .Select(business => new SubscriptionRenewalReminderReadModel(
                business.Id,
                business.Name,
                business.Email,
                "trial_expiring",
                business.TrialEndsAt!.Value))
            .ToListAsync(cancellationToken);
        var subscriptionsExpiring = await dbContext.Businesses
            .AsNoTracking()
            .Where(business => business.IsActive
                && business.SubscriptionEndsAt.HasValue
                && business.SubscriptionEndsAt.Value >= currentTimeUtc
                && business.SubscriptionEndsAt.Value <= subscriptionReminderWindow)
            .Select(business => new SubscriptionRenewalReminderReadModel(
                business.Id,
                business.Name,
                business.Email,
                "subscription_expiring",
                business.SubscriptionEndsAt!.Value))
            .ToListAsync(cancellationToken);

        foreach (var reminder in trialExpiring.Concat(subscriptionsExpiring))
        {
            var targetDate = reminder.TargetAt.Date;
            var exists = await dbContext.BusinessReminderNotifications.AnyAsync(
                row => row.BusinessId == reminder.BusinessId
                    && row.ReminderType == reminder.ReminderType
                    && row.TargetAt >= targetDate
                    && row.TargetAt < targetDate.AddDays(1),
                cancellationToken);
            if (exists)
                continue;

            dbContext.BusinessReminderNotifications.Add(new BusinessReminderNotification
            {
                BusinessId = reminder.BusinessId,
                ReminderType = reminder.ReminderType,
                Message = reminder.ReminderType == "trial_expiring"
                    ? $"Trial expires on {reminder.TargetAt:yyyy-MM-dd}."
                    : $"Subscription expires on {reminder.TargetAt:yyyy-MM-dd}.",
                TargetAt = reminder.TargetAt,
                IsDelivered = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return trialExpiring.Concat(subscriptionsExpiring).ToArray();
    }
}
