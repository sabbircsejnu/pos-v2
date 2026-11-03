namespace RetailPOS.Application.Pricing.Abstractions;

public interface IOutletPriceOverrideWriteAuthorizer
{
    Task<long> AuthorizeWriteAsync(long outletId, CancellationToken cancellationToken);
}
