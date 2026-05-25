using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RetailPOS.Infrastructure.Data;

public class RetailPOSDbContextFactory : IDesignTimeDbContextFactory<RetailPOSDbContext>
{
    public RetailPOSDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RetailPOSDbContext>();
        
        // Default connection string for migrations
        // You can override this with environment variable or command line
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
            ?? "Host=127.0.0.1;Database=retailpos_db;Username=admin;Password=589123Qwe;Port=5435";
        
        optionsBuilder.UseNpgsql(connectionString);

        return new RetailPOSDbContext(optionsBuilder.Options);
    }
}
