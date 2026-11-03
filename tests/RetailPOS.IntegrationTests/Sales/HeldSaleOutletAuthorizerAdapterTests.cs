using Moq;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.API.Services;

namespace RetailPOS.Tests.Sales;

public sealed class HeldSaleOutletAuthorizerAdapterTests
{
    [Fact]
    public async Task OutletAuthorizer_UsesSharedResolverAndReturnsOnlyAuthorizedOutlets()
    {
        var access = new Mock<IUserOutletAccessService>();
        access.Setup(service => service.ResolveAndAuthorizeLocationAsync(11, "outlet"))
            .ReturnsAsync(((long?)11, "outlet"));
        access.Setup(service => service.GetAuthorizedOutletsAsync())
            .ReturnsAsync(new AuthorizedOutletsDto
            {
                Outlets =
                [
                    new AuthorizedLocationDto { Id = 11, Type = "outlet" },
                    new AuthorizedLocationDto { Id = 12, Type = "outlet" }
                ],
                Warehouses = [new AuthorizedLocationDto { Id = 90, Type = "warehouse" }]
            });

        var adapter = new HeldSaleOutletAuthorizerAdapter(access.Object);
        Assert.Equal(11, await adapter.AuthorizeReadAsync(11, CancellationToken.None));
        Assert.Equal(
            new long[] { 11, 12 },
            await adapter.GetAuthorizedOutletIdsAsync(CancellationToken.None));
        access.Verify(service => service.ResolveAndAuthorizeLocationAsync(11, "outlet"), Times.Once);
        access.Verify(service => service.GetAuthorizedOutletsAsync(), Times.Once);
    }

    [Fact]
    public async Task OutletAuthorizer_PropagatesResolverOutletDenials()
    {
        var access = new Mock<IUserOutletAccessService>();
        access.Setup(service => service.ResolveAndAuthorizeLocationAsync(22, "outlet"))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet denied"));
        access.Setup(service => service.ResolveAndAuthorizeLocationAsync(99, "outlet"))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet denied"));
        var adapter = new HeldSaleOutletAuthorizerAdapter(access.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            adapter.AuthorizeReadAsync(22, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            adapter.AuthorizeReadAsync(99, CancellationToken.None));
        access.Verify(service => service.ResolveAndAuthorizeLocationAsync(22, "outlet"), Times.Once);
        access.Verify(service => service.ResolveAndAuthorizeLocationAsync(99, "outlet"), Times.Once);
    }
}
