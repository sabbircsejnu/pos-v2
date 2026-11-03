using Moq;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;

namespace RetailPOS.Tests.SupplierPayments;

public sealed class ReverseSupplierAdvanceUtilizationCommandHandlerTests
{
    [Fact]
    public async Task Handle_AuthorizesBusinessAndOutletBeforeReversingTrimmedReason()
    {
        var store = new Mock<ISupplierAdvanceUtilizationStore>(MockBehavior.Strict);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        var cancellation = new CancellationTokenSource().Token;
        var sequence = new MockSequence();
        authorizer.InSequence(sequence)
            .Setup(value => value.GetBusinessIdAsync(cancellation))
            .ReturnsAsync(1);
        store.InSequence(sequence)
            .Setup(value => value.FindForReversalAsync(660, 1, cancellation))
            .ReturnsAsync(new SupplierAdvanceUtilizationReversalScope(300, 200, 900, 7));
        authorizer.InSequence(sequence)
            .Setup(value => value.EnforceOutletAsync(7, cancellation))
            .ReturnsAsync(7);
        store.InSequence(sequence)
            .Setup(value => value.ReverseAsync(
                660,
                1,
                new DateTime(2026, 3, 20),
                "corrected credit",
                101,
                cancellation))
            .Returns(Task.CompletedTask);
        var handler = new ReverseSupplierAdvanceUtilizationCommandHandler(
            store.Object,
            authorizer.Object);

        await handler.Handle(
            new ReverseSupplierAdvanceUtilizationCommand(
                660,
                new DateTime(2026, 3, 20, 18, 30, 0),
                " corrected credit ",
                101),
            cancellation);

        store.VerifyAll();
        authorizer.VerifyAll();
    }

    [Fact]
    public async Task Handle_RejectsBlankReasonBeforeLoadingUtilization()
    {
        var store = new Mock<ISupplierAdvanceUtilizationStore>(MockBehavior.Strict);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(value => value.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync(1);
        var handler = new ReverseSupplierAdvanceUtilizationCommandHandler(
            store.Object,
            authorizer.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new ReverseSupplierAdvanceUtilizationCommand(660, DateTime.Today, " ", 101),
                CancellationToken.None));

        Assert.Equal("Reversal reason is required.", exception.Message);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_HidesUtilizationOutsideBusinessBeforeOutletAuthorization()
    {
        var store = new Mock<ISupplierAdvanceUtilizationStore>(MockBehavior.Strict);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(value => value.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync(1);
        store.Setup(value => value.FindForReversalAsync(660, 1, CancellationToken.None))
            .ReturnsAsync((SupplierAdvanceUtilizationReversalScope?)null);
        var handler = new ReverseSupplierAdvanceUtilizationCommandHandler(
            store.Object,
            authorizer.Object);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(
                new ReverseSupplierAdvanceUtilizationCommand(660, DateTime.Today, "reason", 101),
                CancellationToken.None));

        Assert.Equal("Effective supplier advance utilization 660 was not found.", exception.Message);
        authorizer.Verify(value => value.EnforceOutletAsync(
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
