namespace RetailPOS.Application.BusinessAdministration.Abstractions;

public sealed record BusinessFeatureSettingChange(
    string FeatureKey,
    bool IsEnabled,
    int? LimitValue);

public enum BusinessOwnerAccessResetStatus
{
    Reset,
    BusinessNotFound,
    OwnerNotFound
}

public sealed record BusinessOwnerAccessResetResult(
    BusinessOwnerAccessResetStatus Status,
    long? UserId,
    string? OwnerEmail);

public interface IBusinessAdministrationWriteStore
{
    Task<bool> SetBusinessActiveAsync(
        long businessId,
        bool isActive,
        CancellationToken cancellationToken);

    Task<bool> UpdateSubscriptionAsync(
        long businessId,
        string? subscriptionPlan,
        DateTime? trialEndsAt,
        DateTime? subscriptionEndsAt,
        int? maxOutlets,
        int? maxUsers,
        CancellationToken cancellationToken);

    Task<bool> UpdateReceiptFooterAsync(
        long businessId,
        bool showPoweredByOnReceipt,
        string? poweredByText,
        CancellationToken cancellationToken);

    Task<bool> UpsertFeatureSettingsAsync(
        long businessId,
        IReadOnlyList<BusinessFeatureSettingChange> features,
        CancellationToken cancellationToken);

    Task<BusinessOwnerAccessResetResult> ResetBusinessOwnerAccessAsync(
        long businessId,
        string tokenHash,
        DateTime now,
        DateTime expiresAt,
        CancellationToken cancellationToken);
}
