using System.Text.Json;
using Moq;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.Queries;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Sales;

public sealed class HeldSaleApplicationTests
{
    [Fact]
    public async Task HoldSale_AuthorizesOutletBeforePersistingAndPreservesProjection()
    {
        var sequence = new MockSequence();
        var authorizer = new Mock<IHeldSaleOutletAuthorizer>(MockBehavior.Strict);
        authorizer.InSequence(sequence)
            .Setup(service => service.AuthorizeWriteAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);
        var store = new Mock<IHeldSaleStore>(MockBehavior.Strict);
        store.InSequence(sequence)
            .Setup(service => service.CreateAsync(
                It.IsAny<HeldSale>(),
                It.IsAny<CancellationToken>()))
            .Returns<HeldSale, CancellationToken>((heldSale, _) =>
            {
                heldSale.Id = 41;
                heldSale.Outlet = new Outlet { Id = 11, Name = "Outlet 11" };
                heldSale.Cashier = new User { Id = 7, Name = "Cashier 7" };
                var persistedItems = JsonSerializer.Deserialize<HeldSaleItemReadModel[]>(
                    heldSale.ItemsJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.Equal("Widget", Assert.Single(persistedItems!).ProductName);
                return Task.FromResult(heldSale);
            });

        var handler = new HoldSaleCommandHandler(store.Object, authorizer.Object);
        var result = await handler.Handle(
            new HoldSaleCommand(
                11,
                7,
                null,
                [new HoldSaleItem(9, "Widget", "W-9", 3, 12m, 2m, "Promo")],
                5m,
                "CASH",
                "note"),
            CancellationToken.None);

        Assert.Equal(41, result.Id);
        Assert.Equal("Outlet 11", result.OutletName);
        Assert.Equal("Cashier 7", result.CashierName);
        Assert.Equal("cash", result.PaymentMethod);
        Assert.Equal(34m, result.Subtotal);
        Assert.Equal("Promo", Assert.Single(result.Items).AppliedRuleName);
        authorizer.VerifyAll();
        store.VerifyAll();
    }

    [Fact]
    public async Task GetByCashier_QueriesOnlyAuthorizedOutlets()
    {
        var authorizer = new Mock<IHeldSaleOutletAuthorizer>();
        authorizer.Setup(service => service.GetAuthorizedOutletIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([11, 12]);
        var store = new Mock<IHeldSaleStore>();
        store.Setup(service => service.GetByCashierAsync(
                7,
                It.Is<IReadOnlyCollection<long>>(outletIds => outletIds.SequenceEqual(new long[] { 11, 12 })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = new GetHeldSalesByCashierQueryHandler(store.Object, authorizer.Object);
        var result = await handler.Handle(new GetHeldSalesByCashierQuery(7), CancellationToken.None);

        Assert.Empty(result.HeldSales);
        Assert.Equal(0, result.TotalCount);
        store.VerifyAll();
    }

    [Fact]
    public async Task GetById_DeniesUnauthorizedOutletBeforeLoadingCart()
    {
        var authorizer = new Mock<IHeldSaleOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeReadAsync(22, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet denied"));
        var store = new Mock<IHeldSaleStore>();
        store.Setup(service => service.GetOutletIdAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(22);

        var handler = new GetHeldSaleByIdQueryHandler(store.Object, authorizer.Object);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new GetHeldSaleByIdQuery(41), CancellationToken.None));

        store.Verify(service => service.GetByIdAsync(41, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetById_ReportsCorruptCartDataInsteadOfReturningAnEmptyCart()
    {
        var authorizer = new Mock<IHeldSaleOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeReadAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);
        var store = new Mock<IHeldSaleStore>();
        store.Setup(service => service.GetOutletIdAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);
        store.Setup(service => service.GetByIdAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HeldSale
            {
                Id = 41,
                OutletId = 11,
                ItemsJson = "{invalid",
                PaymentMethod = "cash"
            });

        var handler = new GetHeldSaleByIdQueryHandler(store.Object, authorizer.Object);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GetHeldSaleByIdQuery(41), CancellationToken.None));

        Assert.Equal("Held sale 41 contains invalid cart data.", error.Message);
        Assert.IsType<JsonException>(error.InnerException);
    }

    [Fact]
    public async Task GetByOutlet_AuthorizesBeforeQuerying()
    {
        var sequence = new MockSequence();
        var authorizer = new Mock<IHeldSaleOutletAuthorizer>(MockBehavior.Strict);
        authorizer.InSequence(sequence)
            .Setup(service => service.AuthorizeReadAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);
        var store = new Mock<IHeldSaleStore>(MockBehavior.Strict);
        store.InSequence(sequence)
            .Setup(service => service.GetByOutletAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = new GetHeldSalesByOutletQueryHandler(store.Object, authorizer.Object);
        var result = await handler.Handle(new GetHeldSalesByOutletQuery(11), CancellationToken.None);

        Assert.Empty(result.HeldSales);
        Assert.Equal(0, result.TotalCount);
        authorizer.VerifyAll();
        store.VerifyAll();
    }

    [Fact]
    public async Task GetByOutlet_DoesNotQueryWhenOutletAuthorizationFails()
    {
        var authorizer = new Mock<IHeldSaleOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeReadAsync(22, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet denied"));
        var store = new Mock<IHeldSaleStore>();

        var handler = new GetHeldSalesByOutletQueryHandler(store.Object, authorizer.Object);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new GetHeldSalesByOutletQuery(22), CancellationToken.None));

        store.Verify(service => service.GetByOutletAsync(22, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_DeniesUnauthorizedOutletBeforeMutation()
    {
        var authorizer = new Mock<IHeldSaleOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeWriteAsync(22, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet denied"));
        var store = new Mock<IHeldSaleStore>();
        store.Setup(service => service.GetOutletIdAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(22);

        var handler = new DeleteHeldSaleCommandHandler(store.Object, authorizer.Object);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new DeleteHeldSaleCommand(41), CancellationToken.None));

        store.Verify(service => service.DeleteAsync(41, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HoldSale_PropagatesCancellationBeforeAuthorizationOrPersistence()
    {
        var authorizer = new Mock<IHeldSaleOutletAuthorizer>(MockBehavior.Strict);
        var store = new Mock<IHeldSaleStore>(MockBehavior.Strict);
        var handler = new HoldSaleCommandHandler(store.Object, authorizer.Object);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(
                new HoldSaleCommand(11, 7, null, [new HoldSaleItem(9, "Widget", "W-9", 1, 10m, 0m, null)],
                    0m, "cash", null),
                cancellation.Token));

        authorizer.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HoldSale_RejectsEmptyCartBeforeAuthorizationOrPersistence()
    {
        var authorizer = new Mock<IHeldSaleOutletAuthorizer>(MockBehavior.Strict);
        var store = new Mock<IHeldSaleStore>(MockBehavior.Strict);
        var handler = new HoldSaleCommandHandler(store.Object, authorizer.Object);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new HoldSaleCommand(11, 7, null, [], 0m, "cash", null),
                CancellationToken.None));

        Assert.Equal("Cannot hold a sale with no items", error.Message);
        authorizer.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

}
