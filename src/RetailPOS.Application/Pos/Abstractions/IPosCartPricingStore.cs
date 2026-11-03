using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Abstractions;

public interface IPosCartPricingStore
{
    Task<PosCartPricingDataReadModel> LoadPricingDataAsync(
        IReadOnlyCollection<long> variantIds,
        IReadOnlyCollection<long> outletIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken);
}

public interface IPosCartPriceCalculator
{
    Task<IReadOnlyList<PosCartPriceReadModel>> CalculateAsync(
        IReadOnlyList<PosCartPriceLine> items,
        bool readFromCache,
        CancellationToken cancellationToken);
}
