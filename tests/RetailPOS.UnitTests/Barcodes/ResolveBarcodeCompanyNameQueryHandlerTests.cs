using Moq;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.Queries;

namespace RetailPOS.UnitTests.Barcodes;

public sealed class ResolveBarcodeCompanyNameQueryHandlerTests
{
    [Fact]
    public async Task Handle_UsesTrimmedRequestedNameBeforeDatabaseLookups()
    {
        var readStore = new Mock<IBarcodeCompanyNameReadStore>();
        var tenantScope = new Mock<IBarcodeTenantScope>();
        var handler = new ResolveBarcodeCompanyNameQueryHandler(readStore.Object, tenantScope.Object);

        var result = await handler.Handle(
            new ResolveBarcodeCompanyNameQuery("  Custom Name  ", 4, 12),
            CancellationToken.None);

        Assert.Equal("Custom Name", result);
        tenantScope.Verify(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()), Times.Never);
        readStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_PrefersBusinessNameOverOutletName()
    {
        var readStore = new Mock<IBarcodeCompanyNameReadStore>();
        var tenantScope = new Mock<IBarcodeTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync(9);
        readStore.Setup(store => store.FindBusinessNameAsync(9, CancellationToken.None))
            .ReturnsAsync("  Business Name  ");
        var handler = new ResolveBarcodeCompanyNameQueryHandler(readStore.Object, tenantScope.Object);

        var result = await handler.Handle(
            new ResolveBarcodeCompanyNameQuery(null, 4, 12),
            CancellationToken.None);

        Assert.Equal("Business Name", result);
        readStore.Verify(store => store.FindBusinessNameAsync(9, CancellationToken.None), Times.Once);
        readStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_UsesExplicitOutletBeforeUserOutlet()
    {
        var readStore = new Mock<IBarcodeCompanyNameReadStore>();
        var tenantScope = new Mock<IBarcodeTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync((long?)null);
        readStore.Setup(store => store.FindOutletNameAsync(4, CancellationToken.None))
            .ReturnsAsync("  Outlet Name  ");
        var handler = new ResolveBarcodeCompanyNameQueryHandler(readStore.Object, tenantScope.Object);

        var result = await handler.Handle(
            new ResolveBarcodeCompanyNameQuery(null, 4, 12),
            CancellationToken.None);

        Assert.Equal("Outlet Name", result);
        readStore.Verify(store => store.FindUserOutletIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        readStore.Verify(store => store.FindOutletNameAsync(4, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_UsesUserOutletWhenNoBusinessOrExplicitOutletNameExists()
    {
        var readStore = new Mock<IBarcodeCompanyNameReadStore>();
        var tenantScope = new Mock<IBarcodeTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync((long?)null);
        readStore.Setup(store => store.FindUserOutletIdAsync(12, CancellationToken.None))
            .ReturnsAsync(4);
        readStore.Setup(store => store.FindOutletNameAsync(4, CancellationToken.None))
            .ReturnsAsync("Outlet Name");
        var handler = new ResolveBarcodeCompanyNameQueryHandler(readStore.Object, tenantScope.Object);

        var result = await handler.Handle(
            new ResolveBarcodeCompanyNameQuery(null, null, 12),
            CancellationToken.None);

        Assert.Equal("Outlet Name", result);
        readStore.Verify(store => store.FindUserOutletIdAsync(12, CancellationToken.None), Times.Once);
        readStore.Verify(store => store.FindOutletNameAsync(4, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNullForApiSettingsFallbackWhenNamesAreUnavailable()
    {
        var readStore = new Mock<IBarcodeCompanyNameReadStore>();
        var tenantScope = new Mock<IBarcodeTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync((long?)null);
        readStore.Setup(store => store.FindUserOutletIdAsync(12, CancellationToken.None))
            .ReturnsAsync((long?)null);
        var handler = new ResolveBarcodeCompanyNameQueryHandler(readStore.Object, tenantScope.Object);

        var result = await handler.Handle(
            new ResolveBarcodeCompanyNameQuery(null, null, 12),
            CancellationToken.None);

        Assert.Null(result);
    }
}
