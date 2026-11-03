using MediatR;
using RetailPOS.Application.Subscriptions.Abstractions;
using RetailPOS.Application.Subscriptions.ReadModels;

namespace RetailPOS.Application.Subscriptions.Commands;

public sealed record ProcessSubscriptionRenewalRemindersCommand(
    DateTime CurrentTimeUtc)
    : IRequest<IReadOnlyList<SubscriptionRenewalReminderReadModel>>;

public sealed class ProcessSubscriptionRenewalRemindersCommandHandler(
    ISubscriptionRenewalReminderStore reminderStore)
    : IRequestHandler<
        ProcessSubscriptionRenewalRemindersCommand,
        IReadOnlyList<SubscriptionRenewalReminderReadModel>>
{
    public Task<IReadOnlyList<SubscriptionRenewalReminderReadModel>> Handle(
        ProcessSubscriptionRenewalRemindersCommand request,
        CancellationToken cancellationToken) =>
        reminderStore.ProcessAsync(request.CurrentTimeUtc, cancellationToken);
}
