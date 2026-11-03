namespace RetailPOS.Application.Subscriptions.ReadModels;

public sealed record SubscriptionRenewalReminderReadModel(
    long BusinessId,
    string BusinessName,
    string? Email,
    string ReminderType,
    DateTime TargetAt);
