using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Application.Customers.Commands;

namespace RetailPOS.Tests.Customers;

public sealed class DeleteCustomerCommandTests
{
    [Fact]
    public async Task DeleteCustomer_RejectsMissingAndSystemCustomers()
    {
        var missingHandler = new DeleteCustomerCommandHandler(new FakeCustomerDeleteStore(snapshot: null, missing: true));
        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => missingHandler.Handle(new DeleteCustomerCommand(42), CancellationToken.None));
        Assert.Equal("Customer with ID 42 not found", missing.Message);

        var systemHandler = new DeleteCustomerCommandHandler(
            new FakeCustomerDeleteStore(new CustomerDeleteSnapshot(true)));
        var system = await Assert.ThrowsAsync<InvalidOperationException>(
            () => systemHandler.Handle(new DeleteCustomerCommand(7), CancellationToken.None));
        Assert.Equal("System customer cannot be deleted", system.Message);
    }

    [Fact]
    public async Task DeleteCustomer_ReturnsRepositoryDeleteResult()
    {
        var store = new FakeCustomerDeleteStore(new CustomerDeleteSnapshot(false));
        var handler = new DeleteCustomerCommandHandler(store);

        var result = await handler.Handle(new DeleteCustomerCommand(7), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(7, store.DeletedId);
    }

    private sealed class FakeCustomerDeleteStore(CustomerDeleteSnapshot? snapshot, bool missing = false)
        : ICustomerDeleteStore
    {
        public long? DeletedId { get; private set; }

        public Task<CustomerDeleteSnapshot?> GetForDeleteAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(missing ? null : snapshot);

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
        {
            DeletedId = id;
            return Task.FromResult(true);
        }
    }
}
