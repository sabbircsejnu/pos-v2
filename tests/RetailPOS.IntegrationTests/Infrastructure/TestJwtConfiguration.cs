using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace RetailPOS.Tests.Infrastructure;

internal static class TestJwtConfiguration
{
    internal const string SigningKey =
        "RetailPOS-test-only-signing-key-not-for-deployment-64-characters-long";

    internal static void Configure(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = SigningKey
            }));
}
