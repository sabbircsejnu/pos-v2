using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RetailPOS.Infrastructure.Data;

public class RetailPOSDbContextFactory : IDesignTimeDbContextFactory<RetailPOSDbContext>
{
    public RetailPOSDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RetailPOSDbContext>();

        // Design-time tooling targets an explicitly chosen database via ConnectionStrings__DefaultConnection.
        // The fallback is an inert placeholder (unresolvable host, no credentials) so model-only operations such as
        // `migrations add` work offline while `database update` can never reach a Development database by accident.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            //connectionString = "Host=design-time.invalid;Database=retailpos_design_time;Username=design_time";
            connectionString = "Host=127.0.0.1;Port=5435;Password=589123Qwe;Database=retailpos_db;Username=admin";

        optionsBuilder.UseNpgsql(connectionString);

        return new RetailPOSDbContext(optionsBuilder.Options);
    }
}
