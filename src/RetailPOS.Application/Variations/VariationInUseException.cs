namespace RetailPOS.Application.Variations;

public sealed class VariationInUseException(
    string message,
    IReadOnlyList<VariationProductReference> products)
    : Exception(message)
{
    public IReadOnlyList<VariationProductReference> Products { get; } = products;
}
