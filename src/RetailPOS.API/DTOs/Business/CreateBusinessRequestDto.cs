namespace RetailPOS.API.DTOs.Business;

public class CreateBusinessRequestDto
{
    public string BusinessName { get; set; } = string.Empty;
    public string? BusinessEmail { get; set; }
    public string? BusinessPhone { get; set; }
    public string? BusinessAddress { get; set; }

    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;

    public string OutletManagerName { get; set; } = string.Empty;
    public string OutletManagerEmail { get; set; } = string.Empty;

    public string SalesPersonName { get; set; } = string.Empty;
    public string SalesPersonEmail { get; set; } = string.Empty;

    public string AccountsAdminName { get; set; } = string.Empty;
    public string AccountsAdminEmail { get; set; } = string.Empty;

    public string DefaultOutletName { get; set; } = "Main Outlet";

    public bool CreateDefaultWarehouse { get; set; } = false;
    public string? DefaultWarehouseName { get; set; }
    public string? WarehouseManagerName { get; set; }
    public string? WarehouseManagerEmail { get; set; }
}

public class CreateBusinessResponseDto
{
    public long BusinessId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public long DefaultOutletId { get; set; }
    public long? DefaultWarehouseId { get; set; }
    public List<OnboardedUserDto> Users { get; set; } = new();
}

public class OnboardedUserDto
{
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;
    public DateTime InvitationExpiresAt { get; set; }
}

public class BusinessSummaryDto
{
    public long BusinessId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string? BusinessEmail { get; set; }
    public string? BusinessPhone { get; set; }
    public bool IsActive { get; set; }
    public string? SubscriptionPlan { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    public int? MaxOutlets { get; set; }
    public int? MaxUsers { get; set; }
    public long UserCount { get; set; }
    public long OutletCount { get; set; }
    public long WarehouseCount { get; set; }
    public string? BusinessOwnerEmail { get; set; }
}

public class UpdateBusinessStatusDto
{
    public bool IsActive { get; set; }
}

public class UpdateBusinessSubscriptionDto
{
    public string? SubscriptionPlan { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    public int? MaxOutlets { get; set; }
    public int? MaxUsers { get; set; }
}

public class ResetBusinessOwnerAccessResponseDto
{
    public long UserId { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public string InvitationToken { get; set; } = string.Empty;
    public DateTime InvitationExpiresAt { get; set; }
}

public class BusinessFeatureSettingDto
{
    public string FeatureKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int? LimitValue { get; set; }
}

public class BusinessFeatureSettingUpsertDto
{
    public string FeatureKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int? LimitValue { get; set; }
}

public class UpdateBusinessFeatureSettingsDto
{
    public List<BusinessFeatureSettingUpsertDto> Features { get; set; } = new();
}
