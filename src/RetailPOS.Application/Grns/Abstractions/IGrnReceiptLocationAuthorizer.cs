namespace RetailPOS.Application.Grns.Abstractions;

/// <summary>
/// Verifies that the current user may receive goods at a location, under the same rules as viewing GRNs:
/// Business Owner and Super Admin across the Business, everyone else on their Step 5 assignments only.
/// Throws <see cref="UnauthorizedAccessException"/> when access is denied.
/// </summary>
public interface IGrnReceiptLocationAuthorizer
{
    Task AuthorizeAsync(
        string locationType,
        long locationId,
        CancellationToken cancellationToken);
}
