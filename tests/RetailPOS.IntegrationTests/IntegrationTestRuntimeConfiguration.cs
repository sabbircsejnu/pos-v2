using System.Runtime.CompilerServices;

namespace RetailPOS.IntegrationTests;

internal static class IntegrationTestRuntimeConfiguration
{
    [ModuleInitializer]
    internal static void ConfigureNpgsqlTimestampBehavior()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        RetailPOS.Tests.Infrastructure.InfrastructureTestSettings.ConfigureIsolatedPostgres();
    }
}
