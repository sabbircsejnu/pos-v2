namespace RetailPOS.API.DTOs.Outlet;

/// <summary>
/// The set of outlets / warehouses the current user is allowed to query data for,
/// plus the user's default selection.
/// </summary>
public class AuthorizedOutletsDto
{
    public List<AuthorizedLocationDto> Outlets { get; set; } = new();
    public List<AuthorizedLocationDto> Warehouses { get; set; } = new();

    /// <summary>The user's active/default outlet — used as the initial filter value.</summary>
    public long? DefaultOutletId { get; set; }

    /// <summary>True when the user has BusinessOwner-level access to every outlet.</summary>
    public bool IsBusinessOwner { get; set; }
}

public class AuthorizedLocationDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "outlet"; // "outlet" | "warehouse"
}
