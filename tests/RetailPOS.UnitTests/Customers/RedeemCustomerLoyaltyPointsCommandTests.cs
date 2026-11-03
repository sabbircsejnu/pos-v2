using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Application.Customers.Commands;

namespace RetailPOS.Tests.Customers;

public sealed class RedeemCustomerLoyaltyPointsCommandTests
{
    [Fact]
    public async Task RedeemPoints_ValidatesAndSubtractsFromCurrentBalance()
    {
        var store = new FakeCustomerLoyaltyStore(10);
        var handler = new RedeemCustomerLoyaltyPointsCommandHandler(store);

        var invalid = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new RedeemCustomerLoyaltyPointsCommand(1, 0),
                CancellationToken.None));
        Assert.Equal("Points must be greater than zero", invalid.Message);
        Assert.False(store.LookupCalled);

        var result = await handler.Handle(
            new RedeemCustomerLoyaltyPointsCommand(1, 4),
            CancellationToken.None);

        Assert.Equal(6, store.SavedPoints);
        Assert.Equal(6, result.LoyaltyPoints);
    }

    [Fact]
    public async Task RedeemPoints_RejectsInsufficientBalanceAndMissingCustomer()
    {
        var insufficientStore = new FakeCustomerLoyaltyStore(3);
        var insufficientHandler = new RedeemCustomerLoyaltyPointsCommandHandler(insufficientStore);
        var insufficient = await Assert.ThrowsAsync<InvalidOperationException>(
            () => insufficientHandler.Handle(
                new RedeemCustomerLoyaltyPointsCommand(1, 4),
                CancellationToken.None));
        Assert.Equal("Insufficient loyalty points. Customer has 3 points but 4 were requested", insufficient.Message);
        Assert.Null(insufficientStore.SavedPoints);

        var missingHandler = new RedeemCustomerLoyaltyPointsCommandHandler(new FakeCustomerLoyaltyStore(null));
        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => missingHandler.Handle(
                new RedeemCustomerLoyaltyPointsCommand(42, 1),
                CancellationToken.None));
        Assert.Equal("Customer with ID 42 not found", missing.Message);
    }

    private sealed class FakeCustomerLoyaltyStore(int? currentPoints) : ICustomerLoyaltyStore
    {
        public bool LookupCalled { get; private set; }
        public int? SavedPoints { get; private set; }

        public Task<CustomerLoyaltySnapshot?> GetForLoyaltyUpdateAsync(
            long id,
            CancellationToken cancellationToken)
        {
            LookupCalled = true;
            return Task.FromResult(currentPoints.HasValue
                ? new CustomerLoyaltySnapshot(id, currentPoints.Value)
                : null);
        }

        public Task<CustomerReadModel> SetLoyaltyPointsAsync(
            long id,
            int loyaltyPoints,
            CancellationToken cancellationToken)
        {
            SavedPoints = loyaltyPoints;
            return Task.FromResult(new CustomerReadModel(
                id,
                "Loyal Customer",
                null,
                null,
                loyaltyPoints,
                DateTime.UnixEpoch,
                0));
        }
    }
}
