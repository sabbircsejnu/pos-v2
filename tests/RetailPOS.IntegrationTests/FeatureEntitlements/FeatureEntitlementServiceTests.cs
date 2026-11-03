using MediatR;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.FeatureEntitlements.Queries;

namespace RetailPOS.IntegrationTests.FeatureEntitlements;

public sealed class FeatureEntitlementServiceTests
{
    [Fact]
    public async Task EnsureFeatureEnabledAsync_ThrowsTheExistingMessageForDisabledFeature()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(
                It.IsAny<IRequest<bool>>(),
                cancellationToken))
            .ReturnsAsync(false);
        var service = new FeatureEntitlementService(sender.Object);

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.EnsureFeatureEnabledAsync(" SALES.Refund ", cancellationToken: cancellationToken));

        Assert.Equal(
            "Feature ' SALES.Refund ' is disabled for this business subscription.",
            exception.Message);
        sender.Verify(s => s.Send(
            It.Is<IsFeatureEnabledQuery>(query =>
                query.FeatureKey == " SALES.Refund " && query.BusinessId == null),
            cancellationToken), Times.Once);
    }
}
