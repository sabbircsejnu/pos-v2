using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Commands;
using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.UnitTests.Inventory;

public sealed class StockAdjustmentCreationCommandHandlerTests
{
    [Fact]
    public async Task DuplicateVariants_AreRejectedBeforeOpeningCreationScope()
    {
        var contextProvider = new Mock<IStockAdjustmentCreateContextProvider>(MockBehavior.Strict);
        var store = new Mock<IStockAdjustmentCreationStore>(MockBehavior.Strict);
        var approvalStore = new Mock<IStockAdjustmentApprovalStore>(MockBehavior.Strict);
        var readStore = new Mock<IStockAdjustmentReadStore>(MockBehavior.Strict);
        var auditWriter = new Mock<IStockAdjustmentAuditWriter>(MockBehavior.Strict);
        var handler = new CreateStockAdjustmentCommandHandler(
            contextProvider.Object,
            store.Object,
            approvalStore.Object,
            readStore.Object,
            auditWriter.Object);
        var command = new CreateStockAdjustmentCommand(
            SubmitForApproval: false,
            LocationId: 5101,
            LocationType: "outlet",
            AdjustedBy: 6101,
            DefaultReason: "Found",
            DefaultNotes: null,
            [
                new StockAdjustmentCreateLine(7101, 1, null, null),
                new StockAdjustmentCreateLine(7101, 2, null, null)
            ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Contains("Duplicate variants are not allowed: 7101", exception.Message);
        contextProvider.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
        approvalStore.VerifyNoOtherCalls();
        readStore.VerifyNoOtherCalls();
        auditWriter.VerifyNoOtherCalls();
    }
}
