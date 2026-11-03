using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Pricing;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Pricing;

public sealed class PriceRuleReadStorePostgresTests
{
    [Fact]
    public async Task SearchAsync_PreservesPostgresCaseInsensitiveNameAndDescriptionSearch()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.PricingRuleRead,
            async database =>
            {
                var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                    .UseNpgsql(database.ConnectionString)
                    .Options;
                await using var dbContext = new RetailPOSDbContext(options);
                await dbContext.Database.EnsureCreatedAsync();
                await TestBusinesses.SeedAsync(dbContext, 1);
                dbContext.PriceRules.AddRange(
                    new PriceRule
                    {
                        Id = 1,
                        BusinessId = 1,
                        Name = "Spring Campaign",
                        Description = "seasonal pricing",
                        RuleType = "global",
                        DiscountType = "percentage",
                        DiscountValue = 10m,
                        IsActive = true
                    },
                    new PriceRule
                    {
                        Id = 2,
                        BusinessId = 1,
                        Name = "Clearance",
                        Description = "Winter clearance offer",
                        RuleType = "global",
                        DiscountType = "fixed",
                        DiscountValue = 3m,
                        IsActive = true
                    });
                await dbContext.SaveChangesAsync();

                IPriceRuleReadStore store = new PriceRuleReadStore(dbContext, new TestBusinessScope(1));
                var nameMatch = await store.SearchAsync(
                    "sPrInG",
                    null,
                    true,
                    1,
                    20,
                    CancellationToken.None);
                var descriptionMatch = await store.SearchAsync(
                    "WINTER",
                    null,
                    true,
                    1,
                    20,
                    CancellationToken.None);

                Assert.Equal(1, nameMatch.TotalCount);
                Assert.Equal("Spring Campaign", Assert.Single(nameMatch.Rules).Name);
                Assert.Equal(1, descriptionMatch.TotalCount);
                Assert.Equal("Clearance", Assert.Single(descriptionMatch.Rules).Name);
            });
    }

    private static string ResolveBaseConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "DefaultConnection is not configured for PostgreSQL integration tests.");
    }
}
