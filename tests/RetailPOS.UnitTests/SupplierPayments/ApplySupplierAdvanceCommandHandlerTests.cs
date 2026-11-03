using Moq;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;

namespace RetailPOS.Tests.SupplierPayments;

public sealed class ApplySupplierAdvanceCommandHandlerTests
{
    [Fact]
    public async Task Handle_AuthorizesBusinessAndOutletBeforeApplyingTrimmedReference()
    {
        var store = new Mock<ISupplierAdvanceUtilizationStore>(MockBehavior.Strict);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        var cancellation = new CancellationTokenSource().Token;
        var date = new DateTime(2026, 3, 15);
        var sequence = new MockSequence();
        authorizer.InSequence(sequence)
            .Setup(value => value.GetBusinessIdAsync(cancellation))
            .ReturnsAsync(1);
        store.InSequence(sequence)
            .Setup(value => value.FindPostedAdvanceAsync(300, 1, cancellation))
            .ReturnsAsync(new SupplierAdvanceUtilizationAdvanceSnapshot(300, 7, 10));
        authorizer.InSequence(sequence)
            .Setup(value => value.EnforceOutletAsync(7, cancellation))
            .ReturnsAsync(7);
        store.InSequence(sequence)
            .Setup(value => value.FindPayableAsync(200, 1, cancellation))
            .ReturnsAsync(new SupplierAdvanceUtilizationPayableSnapshot(200, 10));
        store.InSequence(sequence)
            .Setup(value => value.ApplyAsync(300, 200, 1, 30m, date, "UTIL-1", "notes", 101, cancellation))
            .Returns(Task.CompletedTask);
        var handler = new ApplySupplierAdvanceCommandHandler(store.Object, authorizer.Object);

        await handler.Handle(
            new ApplySupplierAdvanceCommand(300, 200, 30m, date, " UTIL-1 ", "notes", 101),
            cancellation);

        store.VerifyAll();
        authorizer.VerifyAll();
    }

    [Fact]
    public async Task Handle_RejectsInvalidAmountBeforeAdvanceLookup()
    {
        var store = new Mock<ISupplierAdvanceUtilizationStore>(MockBehavior.Strict);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new ApplySupplierAdvanceCommandHandler(store.Object, authorizer.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new ApplySupplierAdvanceCommand(300, 200, 0m, DateTime.Today, "UTIL-1", null, 101),
                CancellationToken.None));

        Assert.Equal(
            "A positive amount and reference number are required to apply a supplier advance.",
            exception.Message);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_RejectsCrossSupplierPayableBeforePosting()
    {
        var store = new Mock<ISupplierAdvanceUtilizationStore>();
        store.Setup(value => value.FindPostedAdvanceAsync(300, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierAdvanceUtilizationAdvanceSnapshot(300, 7, 10));
        store.Setup(value => value.FindPayableAsync(200, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierAdvanceUtilizationPayableSnapshot(200, 11));
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>();
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        authorizer.Setup(value => value.EnforceOutletAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var handler = new ApplySupplierAdvanceCommandHandler(store.Object, authorizer.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new ApplySupplierAdvanceCommand(300, 200, 30m, DateTime.Today, "UTIL-1", null, 101),
                CancellationToken.None));

        Assert.Equal(
            "Supplier advance and payable document must belong to the same supplier.",
            exception.Message);
        store.Verify(value => value.ApplyAsync(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<decimal>(),
            It.IsAny<DateTime>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
