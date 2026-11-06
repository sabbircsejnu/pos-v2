using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Abstractions;

public interface IPosCatalogueReadStore
{
    /// <summary>Active products' variants in the caller's Business, with stock at the criteria outlet when one is given.</summary>
    Task<IReadOnlyList<PosCatalogueItemReadModel>> ListSellableVariantsAsync(
        PosCatalogueCriteria criteria,
        CancellationToken cancellationToken);
}
