using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;

namespace RetailPOS.Tests.Pos;

internal sealed class FixedBusinessScope(long? businessId = 1) : IBusinessScope
{
    public long? EffectiveBusinessId => businessId;

    public long RequireBusinessId() =>
        businessId ?? throw new InvalidOperationException("A business must be selected.");
}

/// <summary>A pricing-cache version whose token is fixed; a null token simulates an unreachable cache backend.</summary>
internal sealed class FixedPricingCacheVersion(string? token = "v1") : IPosPricingCacheVersion
{
    public string? Token { get; set; } = token;
    public List<(long BusinessId, long? OutletId)> Invalidations { get; } = [];

    public Task<string?> GetTokenAsync(long businessId, long? outletId, CancellationToken cancellationToken) =>
        Task.FromResult(Token);

    public Task InvalidateAsync(long businessId, long? outletId, CancellationToken cancellationToken)
    {
        Invalidations.Add((businessId, outletId));
        return Task.CompletedTask;
    }
}

internal sealed class FixedTaxPolicyProvider(SaleTaxPolicy policy, SaleDiscountPolicy? discountPolicy = null) : ISaleCheckoutSettingsProvider
{
    public Task<SaleTaxPolicy> GetTaxPolicyAsync(long businessId, CancellationToken cancellationToken) =>
        Task.FromResult(policy);

    public Task<SaleDiscountPolicy> GetDiscountPolicyAsync(long businessId, CancellationToken cancellationToken) =>
        Task.FromResult(discountPolicy ?? new SaleDiscountPolicy(0m));
}
