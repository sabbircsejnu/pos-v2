using MediatR;
using RetailPOS.Application.Subscriptions.Commands;

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
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var reminders = await sender.Send(
                new ProcessSubscriptionRenewalRemindersCommand(DateTime.UtcNow),
                cancellationToken);
            foreach (var reminder in reminders)
            {
                if (reminder.ReminderType == "trial_expiring")
                {
                    _logger.LogWarning(
                        "Trial reminder: Business {BusinessId} ({BusinessName}) trial expires at {TrialEndsAt}. Contact email: {Email}",
                        reminder.BusinessId,
                        reminder.BusinessName,
                        reminder.TargetAt,
                        reminder.Email ?? "n/a");
                }
                else
                {
                    _logger.LogWarning(
                        "Renewal reminder: Business {BusinessId} ({BusinessName}) subscription expires at {SubscriptionEndsAt}. Contact email: {Email}",
                        reminder.BusinessId,
                        reminder.BusinessName,
                        reminder.TargetAt,
                        reminder.Email ?? "n/a");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription renewal reminder cycle failed.");
        }
    }
}
