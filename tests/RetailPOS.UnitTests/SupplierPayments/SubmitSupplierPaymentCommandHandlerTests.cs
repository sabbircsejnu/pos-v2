using Moq;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Tests.SupplierPayments;

public sealed class SubmitSupplierPaymentCommandHandlerTests
{
    [Fact]
    public async Task Handle_SubmitsScopedDraftThenAuditsAndReloadsProjection()
    {
        var store = new Mock<ISupplierPaymentSubmissionStore>(MockBehavior.Strict);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        var auditWriter = new Mock<ISupplierPaymentSubmissionAuditWriter>(MockBehavior.Strict);
        var cancellation = new CancellationTokenSource().Token;
        var expected = new SupplierPaymentReadModel(
            500, 10, "Supplier", 7, new DateTime(2026, 3, 15), null,
            "cash", 401, 50m, 0m, 0m, "submitted", "PAY-1", null, null);
        var sequence = new MockSequence();
        authorizer.InSequence(sequence)
            .Setup(value => value.GetBusinessIdAsync(cancellation))
            .ReturnsAsync(1);
        store.InSequence(sequence)
            .Setup(value => value.FindForSubmissionAsync(500, 1, cancellation))
            .ReturnsAsync(new SupplierPaymentSubmissionSnapshot(
                500, 7, "DRAFT", 10, 50m, "cash", 401, null, null));
        authorizer.InSequence(sequence)
            .Setup(value => value.EnforceOutletAsync(7, cancellation))
            .ReturnsAsync(7);
        store.InSequence(sequence)
            .Setup(value => value.SubmitAsync(500, 1, 101, cancellation))
            .Returns(Task.CompletedTask);
        auditWriter.InSequence(sequence)
            .Setup(value => value.RecordSubmittedAsync(
                It.Is<SupplierPaymentSubmissionAuditRecord>(record =>
                    record.PaymentId == 500 &&
                    record.BusinessId == 1 &&
                    record.OutletId == 7 &&
                    record.ActorUserId == 101),
                cancellation))
            .Returns(Task.CompletedTask);
        store.InSequence(sequence)
            .Setup(value => value.FindByIdAsync(500, cancellation))
            .ReturnsAsync(expected);
        var handler = new SubmitSupplierPaymentCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        var result = await handler.Handle(new SubmitSupplierPaymentCommand(500, 101), cancellation);

        Assert.Same(expected, result);
        store.VerifyAll();
        authorizer.VerifyAll();
        auditWriter.VerifyAll();
    }

    [Fact]
    public async Task Handle_RejectsNonDraftWithoutUpdatingOrAuditing()
    {
        var store = new Mock<ISupplierPaymentSubmissionStore>();
        store.Setup(value => value.FindForSubmissionAsync(500, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentSubmissionSnapshot(
                500, 7, "posted", 10, 50m, "cash", 401, null, null));
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>();
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        authorizer.Setup(value => value.EnforceOutletAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var auditWriter = new Mock<ISupplierPaymentSubmissionAuditWriter>();
        var handler = new SubmitSupplierPaymentCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new SubmitSupplierPaymentCommand(500, 101), CancellationToken.None));

        Assert.Equal("Only draft supplier payments can be submitted.", exception.Message);
        store.Verify(value => value.SubmitAsync(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        auditWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_HidesPaymentsOutsideBusinessBeforeOutletAuthorization()
    {
        var store = new Mock<ISupplierPaymentSubmissionStore>();
        store.Setup(value => value.FindForSubmissionAsync(500, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupplierPaymentSubmissionSnapshot?)null);
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>();
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var auditWriter = new Mock<ISupplierPaymentSubmissionAuditWriter>();
        var handler = new SubmitSupplierPaymentCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new SubmitSupplierPaymentCommand(500, 101), CancellationToken.None));

        authorizer.Verify(
            value => value.EnforceOutletAsync(It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        store.Verify(
            value => value.SubmitAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        auditWriter.VerifyNoOtherCalls();
    }
}
