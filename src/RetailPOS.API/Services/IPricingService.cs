using RetailPOS.API.DTOs.Pricing;

namespace RetailPOS.API.Services;

public interface IPricingService
{
    // ── Core price calculation ─────────────────────────────────────────────

    /// <summary>
    /// Resolves the full effective price for one cart line.
    /// Safe to call from POS, purchase order entry, and reports.
    /// </summary>
    Task<PriceCalculationResultDto> CalculateAsync(PriceCalculationRequestDto request);

    /// <summary>
    /// Calculates prices for multiple cart lines in a single call.
    /// Use this from the POS screen to avoid N+1 HTTP round trips.
    /// </summary>
    Task<List<PriceCalculationResultDto>> CalculateBatchAsync(PriceCalculationBatchRequestDto request);

    // ── Price Rules (campaign / discount rules) ────────────────────────────

    Task<PriceRuleListDto> SearchRulesAsync(PriceRuleSearchDto searchDto);
    Task<PriceRuleDto> GetRuleByIdAsync(long id);
    Task<PriceRuleDto> CreateRuleAsync(CreatePriceRuleDto dto);
    Task<PriceRuleDto> UpdateRuleAsync(long id, UpdatePriceRuleDto dto);
    Task DeleteRuleAsync(long id);

    // ── Outlet Price Overrides ─────────────────────────────────────────────

    Task<OutletPriceOverrideListDto> GetOverridesByOutletAsync(long outletId);
    Task<OutletPriceOverrideDto> GetOverrideByIdAsync(long id);
    Task<OutletPriceOverrideDto> CreateOverrideAsync(CreateOutletPriceOverrideDto dto);
    Task<OutletPriceOverrideDto> UpdateOverrideAsync(long id, UpdateOutletPriceOverrideDto dto);
    Task DeleteOverrideAsync(long id);
}
