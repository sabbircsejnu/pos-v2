using Moq;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Tests.SupplierPayments;

public sealed class ApproveSupplierPaymentCommandHandlerTests
{
    [Fact]
    public async Task Handle_ApprovesBusinessScopedSubmittedPaymentAfterOutletAuthorization()
    {
        var store = new Mock<ISupplierPaymentApprovalStore>(MockBehavior.Strict);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        var cancellation = new CancellationTokenSource().Token;
        var expected = new SupplierPaymentReadModel(
            500, 10, "Supplier", 7, new DateTime(2026, 3, 15), new DateTime(2026, 3, 15),
            "cash", 401, 50m, 50m, 0m, "posted", "PAY-1", null, 700);
        var sequence = new MockSequence();
        authorizer.InSequence(sequence)
            .Setup(value => value.GetBusinessIdAsync(cancellation))
            .ReturnsAsync(1);
        store.InSequence(sequence)
            .Setup(value => value.FindForApprovalAsync(500, 1, cancellation))
            .ReturnsAsync(new SupplierPaymentApprovalScope(7, "SUBMITTED"));
        authorizer.InSequence(sequence)
            .Setup(value => value.EnforceOutletAsync(7, cancellation))
            .ReturnsAsync(7);
        store.InSequence(sequence)
            .Setup(value => value.ApproveAsync(500, 1, 101, cancellation))
            .ReturnsAsync(expected);
        var handler = new ApproveSupplierPaymentCommandHandler(store.Object, authorizer.Object);

        var result = await handler.Handle(new ApproveSupplierPaymentCommand(500, 101), cancellation);

        Assert.Same(expected, result);
        store.VerifyAll();
        authorizer.VerifyAll();
    }

    [Fact]
    public async Task Handle_RejectsUnsubmittedPaymentBeforeFinancialSideEffects()
    {
        var store = new Mock<ISupplierPaymentApprovalStore>();
        store.Setup(value => value.FindForApprovalAsync(500, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentApprovalScope(7, "draft"));
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>();
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        authorizer.Setup(value => value.EnforceOutletAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var handler = new ApproveSupplierPaymentCommandHandler(store.Object, authorizer.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ApproveSupplierPaymentCommand(500, 101), CancellationToken.None));

        Assert.Equal("Only submitted supplier payments can be approved.", exception.Message);
        store.Verify(value => value.ApproveAsync(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HidesOutOfBusinessPaymentBeforeOutletAuthorization()
    {
        var store = new Mock<ISupplierPaymentApprovalStore>();
        store.Setup(value => value.FindForApprovalAsync(500, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupplierPaymentApprovalScope?)null);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>();
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new ApproveSupplierPaymentCommandHandler(store.Object, authorizer.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new ApproveSupplierPaymentCommand(500, 101), CancellationToken.None));

        authorizer.Verify(
            value => value.EnforceOutletAsync(It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        store.Verify(value => value.ApproveAsync(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
