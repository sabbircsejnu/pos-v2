using Moq;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Tests.SupplierPayments;

public sealed class ReverseSupplierPaymentCommandHandlerTests
{
    [Fact]
    public async Task Handle_RequiresReasonBeforeAnyAuthorizationOrPersistence()
    {
        var store = new Mock<ISupplierPaymentReversalStore>(MockBehavior.Strict);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        var handler = new ReverseSupplierPaymentCommandHandler(store.Object, authorizer.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new ReverseSupplierPaymentCommand(500, DateTime.Today, "  ", 101),
                CancellationToken.None));

        Assert.Equal("Reversal reason is required.", exception.Message);
        store.VerifyNoOtherCalls();
        authorizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_TrimsReasonAndReversesAuthorizedBusinessPayment()
    {
        var store = new Mock<ISupplierPaymentReversalStore>(MockBehavior.Strict);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        var cancellation = new CancellationTokenSource().Token;
        var expected = new SupplierPaymentReadModel(
            500, 10, "Supplier", 7, new DateTime(2026, 3, 15), new DateTime(2026, 3, 15),
            "cash", 401, 50m, 0m, 0m, "reversed", "PAY-1", null, 700);
        var sequence = new MockSequence();
        authorizer.InSequence(sequence)
            .Setup(value => value.GetBusinessIdAsync(cancellation))
            .ReturnsAsync(1);
        store.InSequence(sequence)
            .Setup(value => value.FindForReversalAsync(500, 1, cancellation))
            .ReturnsAsync(new SupplierPaymentReversalScope(7));
        authorizer.InSequence(sequence)
            .Setup(value => value.EnforceOutletAsync(7, cancellation))
            .ReturnsAsync(7);
        store.InSequence(sequence)
            .Setup(value => value.ReverseAsync(
                500,
                1,
                new DateTime(2026, 4, 15),
                "duplicate payment",
                101,
                cancellation))
            .ReturnsAsync(expected);
        var handler = new ReverseSupplierPaymentCommandHandler(store.Object, authorizer.Object);

        var result = await handler.Handle(
            new ReverseSupplierPaymentCommand(
                500,
                new DateTime(2026, 4, 15, 16, 30, 0),
                "  duplicate payment  ",
                101),
            cancellation);

        Assert.Same(expected, result);
        store.VerifyAll();
        authorizer.VerifyAll();
    }

    [Fact]
    public async Task Handle_HidesOutOfBusinessPaymentBeforeOutletAuthorization()
    {
        var store = new Mock<ISupplierPaymentReversalStore>();
        store.Setup(value => value.FindForReversalAsync(500, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupplierPaymentReversalScope?)null);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>();
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new ReverseSupplierPaymentCommandHandler(store.Object, authorizer.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(
                new ReverseSupplierPaymentCommand(500, DateTime.Today, "correction", 101),
                CancellationToken.None));

        authorizer.Verify(
            value => value.EnforceOutletAsync(It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        store.Verify(value => value.ReverseAsync(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<DateTime>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
