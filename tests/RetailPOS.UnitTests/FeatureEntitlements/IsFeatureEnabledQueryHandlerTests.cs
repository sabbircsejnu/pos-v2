using Moq;
using RetailPOS.Application.FeatureEntitlements.Abstractions;
using RetailPOS.Application.FeatureEntitlements.Queries;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.UnitTests.FeatureEntitlements;

public sealed class IsFeatureEnabledQueryHandlerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_ReturnsFalseForBlankFeatureKey(string? featureKey)
    {
        var readStore = new Mock<IFeatureEntitlementReadStore>();
        var tenantScope = new Mock<IFeatureEntitlementScope>();
        var handler = new IsFeatureEnabledQueryHandler(readStore.Object, tenantScope.Object,
            Mock.Of<IHrPayrollCapabilityService>());

        var result = await handler.Handle(
            new IsFeatureEnabledQuery(featureKey!),
            CancellationToken.None);

        Assert.False(result);
        tenantScope.VerifyNoOtherCalls();
        readStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_UsesExplicitBusinessAndNormalizesFeatureKey()
    {
        using var cancellation = new CancellationTokenSource();
        var readStore = new Mock<IFeatureEntitlementReadStore>();
        readStore.Setup(store => store.IsEnabledAsync(12, "sales.refund", cancellation.Token))
            .ReturnsAsync(false);
        var tenantScope = new Mock<IFeatureEntitlementScope>();
        var handler = new IsFeatureEnabledQueryHandler(readStore.Object, tenantScope.Object,
            Mock.Of<IHrPayrollCapabilityService>());

        var result = await handler.Handle(
            new IsFeatureEnabledQuery("  SALES.Refund  ", 12),
            cancellation.Token);

        Assert.False(result);
        tenantScope.VerifyNoOtherCalls();
        readStore.Verify(
            store => store.IsEnabledAsync(12, "sales.refund", cancellation.Token),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UsesCurrentBusinessWhenNoExplicitBusinessIsProvided()
    {
        var readStore = new Mock<IFeatureEntitlementReadStore>();
        readStore.Setup(store => store.IsEnabledAsync(9, "sales.hold", CancellationToken.None))
            .ReturnsAsync(true);
        var tenantScope = new Mock<IFeatureEntitlementScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync(9);
        var handler = new IsFeatureEnabledQueryHandler(readStore.Object, tenantScope.Object,
            Mock.Of<IHrPayrollCapabilityService>());

        var result = await handler.Handle(
            new IsFeatureEnabledQuery("sales.hold"),
            CancellationToken.None);

        Assert.True(result);
        tenantScope.Verify(scope => scope.GetBusinessIdAsync(CancellationToken.None), Times.Once);
        readStore.Verify(store => store.IsEnabledAsync(9, "sales.hold", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_DefaultsToEnabledWhenScopeIsUnrestricted()
    {
        var readStore = new Mock<IFeatureEntitlementReadStore>();
        var tenantScope = new Mock<IFeatureEntitlementScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync((long?)null);
        var handler = new IsFeatureEnabledQueryHandler(readStore.Object, tenantScope.Object,
            Mock.Of<IHrPayrollCapabilityService>());

        var result = await handler.Handle(
            new IsFeatureEnabledQuery("sales.refund"),
            CancellationToken.None);

        Assert.True(result);
        readStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var readStore = new Mock<IFeatureEntitlementReadStore>();
        var tenantScope = new Mock<IFeatureEntitlementScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var handler = new IsFeatureEnabledQueryHandler(readStore.Object, tenantScope.Object,
            Mock.Of<IHrPayrollCapabilityService>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(new IsFeatureEnabledQuery("sales.hold"), cancellation.Token));

        readStore.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("HR.Enabled")]
    [InlineData(" Payroll.Enabled ")]
    [InlineData("Attendance.Enabled")]
    [InlineData("DeviceIntegration.Enabled")]
    public async Task HrKeys_DoNotUseLegacyMissingRowOrUnscopedDefaults(string key)
    {
        var readStore = new Mock<IFeatureEntitlementReadStore>(MockBehavior.Strict);
        var scope = new Mock<IFeatureEntitlementScope>(MockBehavior.Strict);
        var capabilities = new Mock<IHrPayrollCapabilityService>();
        capabilities.Setup(service => service.GetAsync(CancellationToken.None))
            .ReturnsAsync(new HrPayrollCapabilities(42, false, false, false, false));
        var handler = new IsFeatureEnabledQueryHandler(readStore.Object, scope.Object, capabilities.Object);

        Assert.False(await handler.Handle(new IsFeatureEnabledQuery(key), CancellationToken.None));
        readStore.VerifyNoOtherCalls();
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HrKeys_ExplicitDifferentBusinessCannotOverrideValidatedScope()
    {
        var capabilities = new Mock<IHrPayrollCapabilityService>();
        capabilities.Setup(service => service.GetAsync(CancellationToken.None))
            .ReturnsAsync(new HrPayrollCapabilities(42, true, true, true, true));
        var handler = new IsFeatureEnabledQueryHandler(
            Mock.Of<IFeatureEntitlementReadStore>(), Mock.Of<IFeatureEntitlementScope>(), capabilities.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new IsFeatureEnabledQuery(HrPayrollCapabilityKeys.Payroll, 43), CancellationToken.None));
    }
}
