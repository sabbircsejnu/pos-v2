using Microsoft.EntityFrameworkCore;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Capabilities;

namespace RetailPOS.IntegrationTests.FeatureEntitlements;

public sealed class HrPayrollCapabilityFoundationTests
{
    [Fact]
    public async Task ExplicitSettings_AreBusinessFenced_AndRemovalDefaultsOff()
    {
        await using var context = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.BusinessFeatureSettings.AddRange(
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = HrPayrollCapabilityKeys.Hr, IsEnabled = true },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = HrPayrollCapabilityKeys.Payroll, IsEnabled = true },
            new BusinessFeatureSetting { BusinessId = 2, FeatureKey = HrPayrollCapabilityKeys.Payroll, IsEnabled = true });
        await context.SaveChangesAsync();
        var store = new HrPayrollCapabilityReadStore(context);
        var scope = new Mock<IHrPayrollCapabilityScope>();
        scope.Setup(reader => reader.RequireBusinessId()).Returns(1);
        var service = new HrPayrollCapabilityService(scope.Object, store);

        Assert.True((await service.GetAsync(CancellationToken.None)).PayrollEnabled);
        context.BusinessFeatureSettings.Remove(await context.BusinessFeatureSettings.SingleAsync(setting =>
            setting.BusinessId == 1 && setting.FeatureKey == HrPayrollCapabilityKeys.Payroll));
        await context.SaveChangesAsync();
        Assert.False((await service.GetAsync(CancellationToken.None)).PayrollEnabled);
        scope.Setup(reader => reader.RequireBusinessId()).Returns(2);
        Assert.False((await service.GetAsync(CancellationToken.None)).PayrollEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScopeAdapter_NeverBypassesBusinessForAdministrator(bool isSuperAdmin)
    {
        var tenant = new Mock<ITenantAccessService>();
        tenant.SetupGet(context => context.IsSuperAdmin).Returns(isSuperAdmin);
        tenant.Setup(context => context.RequireBusinessId())
            .Throws(new UnauthorizedAccessException("Business scope required."));
        Assert.Throws<UnauthorizedAccessException>(() =>
            new HrPayrollCapabilityScopeAdapter(tenant.Object).RequireBusinessId());
    }
}
