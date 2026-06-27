using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

public class SubscriptionRenewalReminderHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionRenewalReminderHostedService> _logger;

    public SubscriptionRenewalReminderHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionRenewalReminderHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

            var now = DateTime.UtcNow;
            var trialReminderWindow = now.AddDays(3);
            var subscriptionReminderWindow = now.AddDays(7);

            var trialExpiring = await db.Businesses
                .AsNoTracking()
                .Where(b => b.IsActive && b.TrialEndsAt.HasValue && b.TrialEndsAt.Value >= now && b.TrialEndsAt.Value <= trialReminderWindow)
                .Select(b => new { b.Id, b.Name, b.Email, b.TrialEndsAt })
                .ToListAsync(cancellationToken);

            foreach (var tenant in trialExpiring)
            {
                await SaveReminderAsync(
                    db,
                    tenant.Id,
                    "trial_expiring",
                    $"Trial expires on {tenant.TrialEndsAt:yyyy-MM-dd}.",
                    tenant.TrialEndsAt!.Value,
                    cancellationToken);

                _logger.LogWarning(
                    "Trial reminder: Business {BusinessId} ({BusinessName}) trial expires at {TrialEndsAt}. Contact email: {Email}",
                    tenant.Id,
                    tenant.Name,
                    tenant.TrialEndsAt,
                    tenant.Email ?? "n/a");
            }

            var subscriptionsExpiring = await db.Businesses
                .AsNoTracking()
                .Where(b => b.IsActive && b.SubscriptionEndsAt.HasValue && b.SubscriptionEndsAt.Value >= now && b.SubscriptionEndsAt.Value <= subscriptionReminderWindow)
                .Select(b => new { b.Id, b.Name, b.Email, b.SubscriptionEndsAt })
                .ToListAsync(cancellationToken);

            foreach (var tenant in subscriptionsExpiring)
            {
                await SaveReminderAsync(
                    db,
                    tenant.Id,
                    "subscription_expiring",
                    $"Subscription expires on {tenant.SubscriptionEndsAt:yyyy-MM-dd}.",
                    tenant.SubscriptionEndsAt!.Value,
                    cancellationToken);

                _logger.LogWarning(
                    "Renewal reminder: Business {BusinessId} ({BusinessName}) subscription expires at {SubscriptionEndsAt}. Contact email: {Email}",
                    tenant.Id,
                    tenant.Name,
                    tenant.SubscriptionEndsAt,
                    tenant.Email ?? "n/a");
            }

                    await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription renewal reminder cycle failed.");
        }
    }

    private static async Task SaveReminderAsync(
        RetailPOSDbContext db,
        long businessId,
        string reminderType,
        string message,
        DateTime targetAt,
        CancellationToken cancellationToken)
    {
        var targetDate = targetAt.Date;

        var exists = await db.BusinessReminderNotifications.AnyAsync(
            r => r.BusinessId == businessId
                 && r.ReminderType == reminderType
                 && r.TargetAt >= targetDate
                 && r.TargetAt < targetDate.AddDays(1),
            cancellationToken);

        if (exists)
            return;

        db.BusinessReminderNotifications.Add(new BusinessReminderNotification
        {
            BusinessId = businessId,
            ReminderType = reminderType,
            Message = message,
            TargetAt = targetAt,
            IsDelivered = true,
            CreatedAt = DateTime.UtcNow
        });
    }
}
