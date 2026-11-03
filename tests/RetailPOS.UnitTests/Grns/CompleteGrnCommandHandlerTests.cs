using Moq;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.Commands;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Tests.Grns;

public sealed class CompleteGrnCommandHandlerTests
{
    [Fact]
    public async Task OutletDenialOccursBeforeCompletionStoreWrites()
    {
        var store = new Mock<IGrnCompletionStore>();
        store.Setup(port => port.FindScopeAsync(40, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnCompletionScopeReadModel(1, 30, "warehouse", "full"));
        var authorizer = new Mock<IGrnCompletionAuthorizer>();
        authorizer.Setup(port => port.AuthorizeAsync(1, 30, "warehouse", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Warehouse access denied."));
        var handler = new CompleteGrnCommandHandler(store.Object, authorizer.Object);

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new CompleteGrnCommand(40, 101), CancellationToken.None));

        Assert.Equal("Warehouse access denied.", exception.Message);
        store.Verify(
            port => port.FindScopeAsync(40, It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(
            port => port.CompleteAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompletedGrnIsRejectedBeforeTransactionalStoreExecution()
    {
        var store = new Mock<IGrnCompletionStore>();
        store.Setup(port => port.FindScopeAsync(40, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnCompletionScopeReadModel(1, 30, "warehouse", "completed"));
        var authorizer = new Mock<IGrnCompletionAuthorizer>();
        authorizer.Setup(port => port.AuthorizeAsync(1, 30, "warehouse", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new CompleteGrnCommandHandler(store.Object, authorizer.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CompleteGrnCommand(40, 101), CancellationToken.None));

        Assert.Equal("GRN is already completed", exception.Message);
        store.Verify(
            port => port.CompleteAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
