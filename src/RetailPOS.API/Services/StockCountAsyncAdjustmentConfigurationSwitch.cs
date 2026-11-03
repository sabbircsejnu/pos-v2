using RetailPOS.Application.Inventory.StockCounts;

namespace RetailPOS.API.Services;

/// <summary>Reads FeatureFlags:StockCountAsyncAdjustmentEnabled per call so it can be changed without a restart.</summary>
public sealed class StockCountAsyncAdjustmentConfigurationSwitch(IConfiguration configuration)
    : IStockCountAsyncAdjustmentSwitch
{
    public bool Enabled => configuration.GetValue<bool>("FeatureFlags:StockCountAsyncAdjustmentEnabled");
}
