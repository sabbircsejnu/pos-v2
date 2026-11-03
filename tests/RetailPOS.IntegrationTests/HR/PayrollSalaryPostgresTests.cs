using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollSalaryPostgresTests
{
    [Fact]
    public async Task MigrationAndBusinessConstraintsProtectSalarySetupOnPostgres()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false)
                .Build().GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        await using var database = await PostgresTestDatabase.CreateAsync(
            connectionString!, PostgresTestDatabasePrefix.MigrationBaseline);
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options;

        await using (var setup = new RetailPOSDbContext(options))
        {
            Assert.False(setup.Database.HasPendingModelChanges());
            await setup.Database.MigrateAsync();
            var migrations = setup.Database.GetMigrations().ToArray();
            Assert.Contains(migrations, migration =>
                migration.EndsWith("_Phase4BSalaryComponentStructure", StringComparison.Ordinal));
            Assert.Contains(migrations, migration =>
                migration.EndsWith("_Phase4CInitialSalarySetup", StringComparison.Ordinal));
            Assert.Equal(migrations, await setup.Database.GetAppliedMigrationsAsync());
            await setup.Database.MigrateAsync();
            Assert.Empty(await setup.Database.GetPendingMigrationsAsync());

            setup.Businesses.AddRange(new Business { Id = 1, Name = "Payroll one" },
                new Business { Id = 2, Name = "Payroll two" });
            await setup.SaveChangesAsync();
            setup.PayrollSalaryComponents.AddRange(
                Component(1, "BASE"),
                Component(2, "FOREIGN"));
            await setup.SaveChangesAsync();
        }

        long businessOneComponentId;
        long foreignComponentId;
        await using (var query = new RetailPOSDbContext(options))
        {
            businessOneComponentId = await query.PayrollSalaryComponents
                .Where(component => component.BusinessId == 1)
                .Select(component => component.Id).SingleAsync();
            foreignComponentId = await query.PayrollSalaryComponents
                .Where(component => component.BusinessId == 2)
                .Select(component => component.Id).SingleAsync();
            query.PayrollSalaryStructures.Add(new PayrollSalaryStructure
            {
                BusinessId = 1,
                Code = "STANDARD",
                Name = "Standard",
                Lines =
                [
                    new PayrollSalaryStructureLine
                    {
                        BusinessId = 1,
                        SalaryComponentId = businessOneComponentId,
                        DefaultValue = 1000m,
                        CalculationOrder = 1
                    }
                ]
            });
            await query.SaveChangesAsync();
        }

        await using (var foreignReference = new RetailPOSDbContext(options))
        {
            foreignReference.PayrollSalaryStructures.Add(new PayrollSalaryStructure
            {
                BusinessId = 1,
                Code = "INVALID",
                Name = "Invalid cross-Business template",
                Lines =
                [
                    new PayrollSalaryStructureLine
                    {
                        BusinessId = 1,
                        SalaryComponentId = foreignComponentId,
                        DefaultValue = 1m,
                        CalculationOrder = 1
                    }
                ]
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => foreignReference.SaveChangesAsync());
        }

        await using (var duplicate = new RetailPOSDbContext(options))
        {
            duplicate.PayrollSalaryComponents.Add(Component(1, "BASE"));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await using var first = new RetailPOSDbContext(options);
        await using var second = new RetailPOSDbContext(options);
        var firstComponent = await first.PayrollSalaryComponents
            .SingleAsync(component => component.Id == businessOneComponentId);
        var secondComponent = await second.PayrollSalaryComponents
            .SingleAsync(component => component.Id == businessOneComponentId);
        firstComponent.Version++;
        secondComponent.Version++;
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static PayrollSalaryComponent Component(long businessId, string code) => new()
    {
        BusinessId = businessId,
        Code = code,
        Name = code,
        ComponentType = "Earning",
        ValueType = "Fixed"
    };
}
