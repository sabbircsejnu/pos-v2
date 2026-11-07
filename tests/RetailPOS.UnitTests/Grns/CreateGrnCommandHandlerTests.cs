using Moq;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.Commands;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Application.PurchaseOrders.Abstractions;

namespace RetailPOS.Tests.Grns;

public sealed class CreateGrnCommandHandlerTests
{
    [Fact]
    public async Task Handle_RejectsEmptyItemsBeforeReadingOrWriting()
    {
        var store = new Mock<IGrnCreationStore>();
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var auditWriter = new Mock<IGrnCreationAuditWriter>();
        var handler = new CreateGrnCommandHandler(
            store.Object,
            tenantScope.Object,
            auditWriter.Object,
            Mock.Of<IGrnReceiptLocationAuthorizer>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new CreateGrnCommand(20, DateTime.UtcNow, null, null, null, [], 101),
                CancellationToken.None));

        Assert.Equal("GRN must have at least one item", exception.Message);
        store.VerifyNoOtherCalls();
        auditWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_RejectsOverReceiptBeforeCreatingOrAuditing()
    {
        var store = new Mock<IGrnCreationStore>();
        store.Setup(value => value.FindPurchaseOrderAsync(20, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnCreationPurchaseOrderReadModel(
                20,
                1,
                10,
                30,
                "warehouse",
                "approved",
                [new GrnCreationPurchaseOrderItemReadModel(21, 5, 10m, 0m, 0m, false)]));
        store.Setup(value => value.GetCompletedReceivedQuantitiesAsync(
                20,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<long, decimal> { [21] = 4 });
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var auditWriter = new Mock<IGrnCreationAuditWriter>();
        var handler = new CreateGrnCommandHandler(
            store.Object,
            tenantScope.Object,
            auditWriter.Object,
            Mock.Of<IGrnReceiptLocationAuthorizer>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new CreateGrnCommand(
                    20,
                    DateTime.UtcNow,
                    null,
                    null,
                    null,
                    [new CreateGrnItemCommand(21, 2, null, null)],
                    101),
                CancellationToken.None));

        Assert.Contains("Over-receiving not allowed", exception.Message);
        store.Verify(value => value.CreateAsync(
            It.IsAny<GrnCreationRecord>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        auditWriter.VerifyNoOtherCalls();
    }
}
