using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Infrastructure;

public class RetailPosApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"RetailPosTests_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
            services.RemoveAll<RetailPOSDbContext>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));

            services.AddDbContext<RetailPOSDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}
