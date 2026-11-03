using Moq;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.UnitTests.HR;

public sealed class HrPayrollCapabilityServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public async Task ExplicitFlags_EnforceAllDependencies(int flags)
    {
        var settings = HrPayrollCapabilityKeys.All.Select((key, index) =>
            new KeyValuePair<string, bool>(key, (flags & (1 << index)) != 0))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var service = Create(42, settings);

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal(42, result.BusinessId);
        Assert.Equal((flags & 1) != 0, result.HrEnabled);
        Assert.Equal((flags & 3) == 3, result.PayrollEnabled);
        Assert.Equal((flags & 5) == 5, result.AttendanceEnabled);
        Assert.Equal((flags & 13) == 13, result.DeviceIntegrationEnabled);
    }

    [Fact]
    public async Task MissingSettings_AllDefaultOff()
    {
        var result = await Create(42, new Dictionary<string, bool>()).GetAsync(CancellationToken.None);
        Assert.False(result.HrEnabled);
        Assert.False(result.PayrollEnabled);
        Assert.False(result.AttendanceEnabled);
        Assert.False(result.DeviceIntegrationEnabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidBusiness_DeniesBeforeReadingSettings(long businessId)
    {
        var scope = new Mock<IHrPayrollCapabilityScope>();
        scope.Setup(context => context.RequireBusinessId()).Returns(businessId);
        var store = new Mock<IHrPayrollCapabilityReadStore>(MockBehavior.Strict);
        var service = new HrPayrollCapabilityService(scope.Object, store.Object);
        await Assert.ThrowsAsync<BusinessContextRequiredException>(() => service.GetAsync(CancellationToken.None));
        store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(HrPayrollCapabilityKeys.Hr)]
    [InlineData(HrPayrollCapabilityKeys.Payroll)]
    [InlineData(HrPayrollCapabilityKeys.Attendance)]
    [InlineData(HrPayrollCapabilityKeys.DeviceIntegration)]
    public async Task EnsureEnabled_MissingFlagDenies(string key) =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Create(42, new Dictionary<string, bool>()).EnsureEnabledAsync(key, CancellationToken.None));

    [Fact]
    public async Task UnknownCapability_IsAnExplicitError() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Create(42, new Dictionary<string, bool>()).EnsureEnabledAsync("sales.hold", CancellationToken.None));

    [Fact]
    public async Task NoCache_DisablingPayrollIsVisibleOnNextRead()
    {
        var settings = new Dictionary<string, bool>
        {
            [HrPayrollCapabilityKeys.Hr] = true,
            [HrPayrollCapabilityKeys.Payroll] = true
        };
        var service = Create(42, settings);
        Assert.True((await service.GetAsync(CancellationToken.None)).PayrollEnabled);
        settings[HrPayrollCapabilityKeys.Payroll] = false;
        Assert.False((await service.GetAsync(CancellationToken.None)).PayrollEnabled);
    }

    private static HrPayrollCapabilityService Create(long businessId, IReadOnlyDictionary<string, bool> settings)
    {
        var scope = new Mock<IHrPayrollCapabilityScope>();
        scope.Setup(context => context.RequireBusinessId()).Returns(businessId);
        var store = new Mock<IHrPayrollCapabilityReadStore>();
        store.Setup(reader => reader.GetExplicitSettingsAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        return new HrPayrollCapabilityService(scope.Object, store.Object);
    }
}
