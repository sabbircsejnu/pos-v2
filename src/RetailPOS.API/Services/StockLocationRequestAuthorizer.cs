namespace RetailPOS.API.Services;

internal static class StockLocationRequestAuthorizer
{
    public static async Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeAsync(
        IUserOutletAccessService outletAccess,
        long? locationId,
        string? locationType,
        bool locationTypeWasSupplied)
    {
        if (!locationId.HasValue && !locationTypeWasSupplied)
        {
            return (null, null);
        }

        if (!locationId.HasValue || locationId.Value <= 0 || string.IsNullOrWhiteSpace(locationType))
        {
            throw new ArgumentException("A positive locationId and locationType must be supplied together.");
        }

        var normalizedLocationType = locationType!.Trim().ToLowerInvariant();
        if (normalizedLocationType is not ("outlet" or "warehouse"))
        {
            throw new ArgumentException("locationType must be either 'outlet' or 'warehouse'.");
        }

        return await outletAccess.ResolveAndAuthorizeLocationAsync(
            locationId.Value,
            normalizedLocationType);
    }
}
