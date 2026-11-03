using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Application.Customers.Commands;

namespace RetailPOS.Tests.Customers;

public sealed class AddCustomerLoyaltyPointsCommandTests
{
    [Fact]
    public async Task AddPoints_ValidatesBeforeLookupAndAddsToCurrentBalance()
    {
        var store = new FakeCustomerLoyaltyStore(7);
        var handler = new AddCustomerLoyaltyPointsCommandHandler(store);

        var invalid = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new AddCustomerLoyaltyPointsCommand(1, 0),
                CancellationToken.None));
        Assert.Equal("Points must be greater than zero", invalid.Message);
        Assert.False(store.LookupCalled);

        var result = await handler.Handle(
            new AddCustomerLoyaltyPointsCommand(1, 5),
            CancellationToken.None);

        Assert.Equal(12, store.SavedPoints);
        Assert.Equal(12, result.LoyaltyPoints);
    }

    [Fact]
    public async Task AddPoints_ThrowsNotFoundWhenCustomerDoesNotExist()
    {
        var handler = new AddCustomerLoyaltyPointsCommandHandler(new FakeCustomerLoyaltyStore(null));

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(
                new AddCustomerLoyaltyPointsCommand(42, 1),
                CancellationToken.None));

        Assert.Equal("Customer with ID 42 not found", exception.Message);
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
