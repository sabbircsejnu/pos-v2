using RetailPOS.Application.Subscriptions.ReadModels;

namespace RetailPOS.Application.Subscriptions.Abstractions;

public interface ISubscriptionRenewalReminderStore
{
    Task<IReadOnlyList<SubscriptionRenewalReminderReadModel>> ProcessAsync(
        DateTime currentTimeUtc,
        CancellationToken cancellationToken);
}
