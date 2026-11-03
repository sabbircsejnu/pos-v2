namespace RetailPOS.Application.BusinessAdministration.ReadModels;

public sealed record BusinessSummaryReadModel(
    long BusinessId,
    string BusinessName,
    string? BusinessEmail,
    string? BusinessPhone,
    bool IsActive,
    string? SubscriptionPlan,
    DateTime? TrialEndsAt,
    DateTime? SubscriptionEndsAt,
    int? MaxOutlets,
    int? MaxUsers,
    long UserCount,
    long OutletCount,
    long WarehouseCount,
    string? BusinessOwnerEmail);

public sealed record BusinessFeatureSettingReadModel(
    string FeatureKey,
    bool IsEnabled,
    int? LimitValue);

public sealed record ResetBusinessOwnerAccessReadModel(
    long UserId,
    string OwnerEmail,
    string InvitationToken,
    DateTime InvitationExpiresAt);
