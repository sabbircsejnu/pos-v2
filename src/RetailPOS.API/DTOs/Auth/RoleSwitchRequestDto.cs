namespace RetailPOS.API.DTOs.Auth;

public class UserContextOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UserContextOptionsDto
{
    public bool CanSwitch { get; set; }
    public bool AllowAllLocations { get; set; }
    public List<UserContextOptionDto> Roles { get; set; } = new();
    public List<UserContextOptionDto> Outlets { get; set; } = new();
    public List<UserContextOptionDto> Warehouses { get; set; } = new();
}

public class RoleSwitchRequestDto
{
    public string ActingRole { get; set; } = string.Empty;
    public long? ActingOutletId { get; set; }

    /// <summary>outlet | warehouse | all. Omit for the legacy outlet-only request.</summary>
    public string? ActingLocationType { get; set; }

    /// <summary>The outlet or warehouse selected by <see cref="ActingLocationType"/>.</summary>
    public long? ActingLocationId { get; set; }
}
