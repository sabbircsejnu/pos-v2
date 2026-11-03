using RetailPOS.Application.AccountingJournals.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailPOS.API.Services;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Pos;

namespace RetailPOS.Tests.Infrastructure;

public class RetailPosApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"RetailPosTests_{Guid.NewGuid()}";
    private readonly string _settingsFilePath = Path.Combine(Path.GetTempPath(), $"RetailPosTests_{Guid.NewGuid()}_system-settings.json");
    protected virtual bool UseProductionAuditInterceptor => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = TestJwtConfiguration.SigningKey,
                ["FeatureFlags:StockCountPhase3Enabled"] = "true",
                ["SystemSettings:FilePath"] = _settingsFilePath
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
            services.RemoveAll<RetailPOSDbContext>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));

            services.AddDbContext<RetailPOSDbContext>((serviceProvider, options) =>
            {
                options.UseInMemoryDatabase(_dbName);
                options.AddPosPricingCacheInvalidation(serviceProvider);
                if (UseProductionAuditInterceptor)
                    options.AddInterceptors(
                        serviceProvider.GetRequiredService<AuditChangeTrackerInterceptor>());
                options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.RemoveAll<IJournalNumberAllocator>();
            services.AddSingleton<IJournalNumberAllocator, TestJournalNumberAllocator>();
        });
    }
}

public sealed class AuditEnabledRetailPosApiFactory : RetailPosApiFactory
{
    protected override bool UseProductionAuditInterceptor => true;
}
