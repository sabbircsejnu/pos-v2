namespace RetailPOS.Application.BusinessAdministration.ReadModels;

public sealed record OnboardedUserReadModel(
    long UserId,
    string Name,
    string Email,
    string Role,
    string InvitationToken,
    DateTime InvitationExpiresAt);

public sealed record BusinessOnboardingReadModel(
    long BusinessId,
    string BusinessName,
    long DefaultOutletId,
    long? DefaultWarehouseId,
    IReadOnlyList<OnboardedUserReadModel> Users);
