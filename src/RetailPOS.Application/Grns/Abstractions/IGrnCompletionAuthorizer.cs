namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnCompletionAuthorizer
{
    Task<long?> AuthorizeAsync(
        long? businessId,
        long locationId,
        string locationType,
        CancellationToken cancellationToken);
}
