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
