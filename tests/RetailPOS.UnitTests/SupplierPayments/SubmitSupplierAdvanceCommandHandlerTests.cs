using Moq;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.UnitTests.SupplierPayments;

public sealed class SubmitSupplierAdvanceCommandHandlerTests
{
    [Fact]
    public async Task Handle_EnforcesBusinessThenOutletAndRecordsSubmittedActor()
    {
        var sequence = new MockSequence();
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        authorizer.InSequence(sequence)
            .Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        authorizer.InSequence(sequence)
            .Setup(value => value.EnforceOutletAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var store = new Mock<ISupplierAdvanceSubmissionStore>(MockBehavior.Strict);
        var snapshot = new SupplierAdvanceSubmissionSnapshot(
            800, 7, "draft", 10, 50m, 50m, 0m, "cash", 401, null, null);
        store.Setup(value => value.FindForSubmissionAsync(800, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        store.Setup(value => value.SubmitAsync(800, 1, 101, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var expected = new SupplierAdvanceReadModel(
            800, 10, "Supplier", 7, new DateTime(2026, 3, 15),
            "cash", 401, 50m, 50m, 0m, "submitted", "ADV-1", null, null);
        store.Setup(value => value.FindByIdAsync(800, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        SupplierAdvanceSubmissionAuditRecord? auditRecord = null;
        var auditWriter = new Mock<ISupplierAdvanceSubmissionAuditWriter>(MockBehavior.Strict);
        auditWriter.Setup(value => value.RecordSubmittedAsync(
                It.IsAny<SupplierAdvanceSubmissionAuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Callback<SupplierAdvanceSubmissionAuditRecord, CancellationToken>((record, _) =>
                auditRecord = record)
            .Returns(Task.CompletedTask);
        var handler = new SubmitSupplierAdvanceCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        var result = await handler.Handle(
            new SubmitSupplierAdvanceCommand(800, 101),
            CancellationToken.None);

        Assert.Same(expected, result);
        Assert.Equal(800, auditRecord!.AdvanceId);
        Assert.Equal(1, auditRecord.BusinessId);
        Assert.Equal(7, auditRecord.OutletId);
        Assert.Equal(101, auditRecord.ActorUserId);
        store.VerifyAll();
        authorizer.VerifyAll();
        auditWriter.VerifyAll();
    }

    [Fact]
    public async Task Handle_RejectsNonDraftAfterOutletAuthorizationWithoutWriteOrAudit()
    {
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        authorizer.Setup(value => value.EnforceOutletAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var store = new Mock<ISupplierAdvanceSubmissionStore>(MockBehavior.Strict);
        store.Setup(value => value.FindForSubmissionAsync(800, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierAdvanceSubmissionSnapshot(
                800, 7, "posted", 10, 50m, 50m, 0m, "cash", 401, 900, null));
        var auditWriter = new Mock<ISupplierAdvanceSubmissionAuditWriter>(MockBehavior.Strict);
        var handler = new SubmitSupplierAdvanceCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new SubmitSupplierAdvanceCommand(800, 101), CancellationToken.None));

        Assert.Equal("Only draft supplier advances can be submitted.", exception.Message);
        store.Verify(value => value.SubmitAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        auditWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_DoesNotAuthorizeOutletForAdvanceOutsideBusiness()
    {
        var authorizer = new Mock<ISupplierPaymentWorkflowAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(value => value.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var store = new Mock<ISupplierAdvanceSubmissionStore>(MockBehavior.Strict);
        store.Setup(value => value.FindForSubmissionAsync(800, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupplierAdvanceSubmissionSnapshot?)null);
        var auditWriter = new Mock<ISupplierAdvanceSubmissionAuditWriter>(MockBehavior.Strict);
        var handler = new SubmitSupplierAdvanceCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new SubmitSupplierAdvanceCommand(800, 101), CancellationToken.None));

        authorizer.Verify(value => value.EnforceOutletAsync(
            It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(value => value.SubmitAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        auditWriter.VerifyNoOtherCalls();
    }
}
