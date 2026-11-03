using Moq;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.SupplierPayments;

public sealed class CreateSupplierAdvanceCommandHandlerTests
{
    [Fact]
    public async Task Handle_CreatesDraftWithNormalizedDatesAndAudit()
    {
        var store = new Mock<ISupplierAdvanceCreationStore>(MockBehavior.Strict);
        store.Setup(value => value.SupplierExistsAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(value => value.PaymentAccountExistsAsync(1, 401, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SupplierAdvance? saved = null;
        var expected = new SupplierAdvanceReadModel(
            800, 10, "Supplier", 7, new DateTime(2026, 3, 15),
            "cash", 401, 50m, 50m, 0m, "draft", "ADV-1", "note", null);
        store.Setup(value => value.CreateAsync(
                It.IsAny<SupplierAdvance>(),
                It.IsAny<CancellationToken>()))
            .Callback<SupplierAdvance, CancellationToken>((advance, _) =>
            {
                advance.Id = 800;
                saved = advance;
            })
            .ReturnsAsync(expected);
        var authorizer = new Mock<ISupplierPaymentCreationAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(value => value.AuthorizeAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentCreationScope(1, 7));
        SupplierAdvanceCreationAuditRecord? auditRecord = null;
        var auditWriter = new Mock<ISupplierAdvanceCreationAuditWriter>(MockBehavior.Strict);
        auditWriter.Setup(value => value.RecordCreatedAsync(
                It.IsAny<SupplierAdvanceCreationAuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Callback<SupplierAdvanceCreationAuditRecord, CancellationToken>((record, _) =>
                auditRecord = record)
            .Returns(Task.CompletedTask);
        var handler = new CreateSupplierAdvanceCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        var result = await handler.Handle(
            new CreateSupplierAdvanceCommand(
                10,
                7,
                new DateTime(2026, 3, 15, 18, 20, 0),
                " cash ",
                401,
                50m,
                " ADV-1 ",
                " note ",
                101),
            CancellationToken.None);

        Assert.Same(expected, result);
        Assert.NotNull(saved);
        Assert.Equal(1, saved.BusinessId);
        Assert.Equal(7, saved.OutletId);
        Assert.Equal(new DateTime(2026, 3, 15), saved.PaymentDate);
        Assert.Equal(saved.PaymentDate, saved.BusinessDate);
        Assert.Equal(saved.PaymentDate, saved.PostingDate);
        Assert.Equal("cash", saved.PaymentMethod);
        Assert.Equal("ADV-1", saved.ReferenceNumber);
        Assert.Equal("note", saved.Notes);
        Assert.Equal("1:10:20260315:ADV-1", saved.IdempotencyKey);
        Assert.Equal(50m, saved.RemainingAmount);
        Assert.Equal(0m, saved.AppliedAmount);
        Assert.Equal("draft", saved.Status);
        Assert.Equal(101, saved.CreatedBy);
        Assert.Equal(101, saved.UpdatedBy);
        Assert.Equal(101, auditRecord!.ActorUserId);
        Assert.Equal(800, auditRecord.AdvanceId);
        store.VerifyAll();
        authorizer.VerifyAll();
        auditWriter.VerifyAll();
    }

    [Fact]
    public async Task Handle_RejectsOutOfBusinessSupplierBeforePaymentAccountOrPersistence()
    {
        var store = new Mock<ISupplierAdvanceCreationStore>(MockBehavior.Strict);
        store.Setup(value => value.SupplierExistsAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var authorizer = new Mock<ISupplierPaymentCreationAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(value => value.AuthorizeAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentCreationScope(1, null));
        var auditWriter = new Mock<ISupplierAdvanceCreationAuditWriter>(MockBehavior.Strict);
        var handler = new CreateSupplierAdvanceCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new CreateSupplierAdvanceCommand(
                    10, null, new DateTime(2026, 3, 15), "cash", 401,
                    50m, null, null, 101),
                CancellationToken.None));

        Assert.Equal("Supplier 10 was not found.", exception.Message);
        store.Verify(value => value.PaymentAccountExistsAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(value => value.CreateAsync(
            It.IsAny<SupplierAdvance>(), It.IsAny<CancellationToken>()), Times.Never);
        auditWriter.VerifyNoOtherCalls();
    }
}
