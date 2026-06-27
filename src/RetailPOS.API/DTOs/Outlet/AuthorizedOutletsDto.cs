namespace RetailPOS.API.DTOs.Outlet;

/// <summary>
/// The set of outlets / warehouses the current user is allowed to query data for,
/// plus the user's default selection.
/// </summary>
public class AuthorizedOutletsDto
{
    public List<AuthorizedLocationDto> Outlets { get; set; } = new();
    public List<AuthorizedLocationDto> Warehouses { get; set; } = new();
    public List<AuthorizedLocationDto> DestinationOutlets { get; set; } = new();
    public List<AuthorizedLocationDto> DestinationWarehouses { get; set; } = new();

    /// <summary>The user's active/default outlet — used as the initial filter value.</summary>
    public long? DefaultOutletId { get; set; }

    /// <summary>True when the user has BusinessOwner-level access to every outlet.</summary>
    public bool IsBusinessOwner { get; set; }

    /// <summary>True when the user can operate on every outlet and warehouse in scope.</summary>
    public bool IsGlobalAccess { get; set; }

    /// <summary>resolved inventory access scope value (assigned_only | specific_locations | all_locations).</summary>
    public string AccessScope { get; set; } = "assigned_only";

    /// <summary>Primary default location used for assigned scope users.</summary>
    public long? DefaultLocationId { get; set; }
    public string? DefaultLocationType { get; set; }
}

public class AuthorizedLocationDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "outlet"; // "outlet" | "warehouse"
}
