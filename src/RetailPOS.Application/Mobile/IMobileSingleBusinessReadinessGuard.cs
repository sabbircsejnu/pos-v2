namespace RetailPOS.Application.Mobile;

/// <summary>
/// Mobile endpoints are only enabled while the deployment has exactly one active Business owning every Outlet.
/// The check is a persistence concern, so it is defined here and implemented in Infrastructure.
/// </summary>
public interface IMobileSingleBusinessReadinessGuard
{
    Task<bool> IsReadyAsync(long businessId, CancellationToken cancellationToken);
}
