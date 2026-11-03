using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace RetailPOS.Tests.Infrastructure;

/// <summary>API factory with the legacy loyalty integer-balance endpoints switched on (they are off by default).</summary>
public sealed class LegacyLoyaltyEnabledApiFactory : RetailPosApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:LegacyLoyaltyPointsMutationEnabled"] = "true"
            }));
    }
}
