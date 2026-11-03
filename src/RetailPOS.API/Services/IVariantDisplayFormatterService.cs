namespace RetailPOS.API.Services;

public interface IVariantDisplayFormatterService
{
    Task<string?> FormatAsync(
        string? rawAttributes,
        CancellationToken cancellationToken = default);
}
