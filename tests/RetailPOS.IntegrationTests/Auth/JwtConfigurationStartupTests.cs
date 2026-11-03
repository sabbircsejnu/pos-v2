using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Auth;

public sealed class JwtConfigurationStartupTests
{
    [Fact]
    public void MissingSigningKey_RejectsStartupWithExplicitConfigurationError()
    {
        using var baseFactory = new RetailPosApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:Secret"] = ""
                })));

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("Configure JwtSettings:Secret", exception.Message);
    }
}
