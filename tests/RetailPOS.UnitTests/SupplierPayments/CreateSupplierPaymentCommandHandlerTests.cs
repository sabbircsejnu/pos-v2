using Moq;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.SupplierPayments;

public sealed class CreateSupplierPaymentCommandHandlerTests
{
    [Fact]
    public async Task Handle_CreatesDraftAllocationsAndAuditWithNormalizedValues()
    {
        var store = new Mock<ISupplierPaymentCreationStore>();
        store.Setup(value => value.SupplierExistsAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(value => value.PaymentAccountExistsAsync(1, 401, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        SupplierPayment? savedPayment = null;
        store.Setup(value => value.CreateDraftAsync(
                It.IsAny<SupplierPayment>(),
                It.IsAny<CancellationToken>()))
            .Callback<SupplierPayment, CancellationToken>((payment, _) =>
            {
                payment.Id = 500;
                savedPayment = payment;
            })
            .Returns(Task.CompletedTask);
        store.Setup(value => value.FindPayableDocumentAsync(1, 10, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPayableDocument
            {
                Id = 100,
                BusinessId = 1,
                SupplierId = 10,
                SourceReference = "GRN-100"
            });
        store.Setup(value => value.GetPayableOutstandingAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60m);
        store.Setup(value => value.FindBillAsync(1, 10, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Bill { Id = 200, SupplierId = 10, AmountDue = 40m });
        IReadOnlyList<SupplierPaymentAllocation>? savedAllocations = null;
        store.Setup(value => value.CreateAllocationsAsync(
                It.IsAny<IReadOnlyList<SupplierPaymentAllocation>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<SupplierPaymentAllocation>, CancellationToken>((allocations, _) =>
                savedAllocations = allocations)
            .Returns(Task.CompletedTask);
        var expected = new SupplierPaymentReadModel(
            500, 10, "Supplier", 7, new DateTime(2026, 3, 15), new DateTime(2026, 3, 15),
            "bank", 401, 50m, 0m, 0m, "draft", "PAY-1", "note", null);
        store.Setup(value => value.FindByIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var authorizer = new Mock<ISupplierPaymentCreationAuthorizer>();
        authorizer.Setup(value => value.AuthorizeAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentCreationScope(1, 7));
        SupplierPaymentCreationAuditRecord? auditRecord = null;
        var auditWriter = new Mock<ISupplierPaymentCreationAuditWriter>();
        auditWriter.Setup(value => value.RecordCreatedAsync(
                It.IsAny<SupplierPaymentCreationAuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Callback<SupplierPaymentCreationAuditRecord, CancellationToken>((record, _) =>
                auditRecord = record)
            .Returns(Task.CompletedTask);
        var handler = new CreateSupplierPaymentCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        var result = await handler.Handle(
            new CreateSupplierPaymentCommand(
                10,
                7,
                new DateTime(2026, 3, 15, 16, 30, 0),
                " bank ",
                401,
                50m,
                " PAY-1 ",
                " note ",
                [
                    new CreateSupplierPaymentAllocationCommand(0, 100, 20m),
                    new CreateSupplierPaymentAllocationCommand(200, null, 10m)
                ],
                101),
            CancellationToken.None);

        Assert.Same(expected, result);
        Assert.NotNull(savedPayment);
        Assert.Equal(1, savedPayment.BusinessId);
        Assert.Equal(7, savedPayment.OutletId);
        Assert.Equal(new DateTime(2026, 3, 15), savedPayment.PaymentDate);
        Assert.Equal("bank", savedPayment.PaymentMethod);
        Assert.Equal("PAY-1", savedPayment.ReferenceNumber);
        Assert.Equal("1:10:20260315:PAY-1", savedPayment.IdempotencyKey);
        Assert.Equal(2, savedAllocations!.Count);
        Assert.Equal(60m, savedAllocations[0].OutstandingBeforeAllocation);
        Assert.Equal(40m, savedAllocations[0].OutstandingAfterAllocation);
        Assert.Equal("GRN-100", savedAllocations[0].SourceReference);
        Assert.Equal(40m, savedAllocations[1].OutstandingBeforeAllocation);
        Assert.Equal(30m, savedAllocations[1].OutstandingAfterAllocation);
        Assert.Equal(30m, auditRecord!.AllocationTotal);
        Assert.Equal(2, auditRecord.AllocationCount);
        Assert.Equal(101, auditRecord.ActorUserId);
        store.Verify(value => value.CreateDraftAsync(
            It.IsAny<SupplierPayment>(),
            It.IsAny<CancellationToken>()), Times.Once);
        auditWriter.Verify(value => value.RecordCreatedAsync(
            It.IsAny<SupplierPaymentCreationAuditRecord>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PersistsDraftBeforeRejectingAllocationTotalAboveAmount()
    {
        var store = new Mock<ISupplierPaymentCreationStore>();
        store.Setup(value => value.SupplierExistsAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(value => value.PaymentAccountExistsAsync(1, 401, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(value => value.CreateDraftAsync(
                It.IsAny<SupplierPayment>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var authorizer = new Mock<ISupplierPaymentCreationAuthorizer>();
        authorizer.Setup(value => value.AuthorizeAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupplierPaymentCreationScope(1, null));
        var auditWriter = new Mock<ISupplierPaymentCreationAuditWriter>();
        var handler = new CreateSupplierPaymentCommandHandler(
            store.Object,
            authorizer.Object,
            auditWriter.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new CreateSupplierPaymentCommand(
                    10,
                    null,
                    new DateTime(2026, 3, 17),
                    "bank",
                    401,
                    100m,
                    null,
                    null,
                    [
                        new CreateSupplierPaymentAllocationCommand(1, null, 75m),
                        new CreateSupplierPaymentAllocationCommand(1, null, 40m)
                    ],
                    101),
                CancellationToken.None));

        Assert.Equal("Allocation total cannot exceed the payment amount.", exception.Message);
        store.Verify(value => value.CreateDraftAsync(
            It.IsAny<SupplierPayment>(),
            It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(value => value.CreateAllocationsAsync(
            It.IsAny<IReadOnlyList<SupplierPaymentAllocation>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        auditWriter.VerifyNoOtherCalls();
    }
}
