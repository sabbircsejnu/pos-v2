using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Application.Customers.Commands;

namespace RetailPOS.Tests.Customers;

public sealed class UpdateCustomerCommandTests
{
    [Fact]
    public async Task UpdateCustomer_ValidatesAndNormalizesBeforePersistence()
    {
        var store = new FakeCustomerUpdateStore();
        var handler = new UpdateCustomerCommandHandler(store);

        var result = await handler.Handle(
            new UpdateCustomerCommand(7, "  Ada  ", " 01712-345678 ", " Ada@Example.Test "),
            CancellationToken.None);

        Assert.Equal(
            new CustomerUpdateValues(7, "Ada", "01712-345678", "ada@example.test", "+8801712345678"),
            store.UpdatedValues);
        Assert.Equal("Ada", result.Name);
        Assert.Equal((7, "+8801712345678"), store.LastPhoneCheck);
        Assert.Equal((7, " Ada@Example.Test "), store.LastEmailCheck);
    }

    [Fact]
    public async Task UpdateCustomer_AllowsUnchangedLegacyPhoneThatCannotBeNormalized()
    {
        var store = new FakeCustomerUpdateStore();
        var handler = new UpdateCustomerCommandHandler(store);

        await handler.Handle(
            new UpdateCustomerCommand(7, "Renamed", "555-0007", null),
            CancellationToken.None);

        Assert.Null(store.LastPhoneCheck);
        Assert.Null(store.UpdatedValues!.PhoneNormalized);
    }

    [Fact]
    public async Task UpdateCustomer_RejectsChangedPhoneThatCannotBeNormalized()
    {
        var store = new FakeCustomerUpdateStore();
        var handler = new UpdateCustomerCommandHandler(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new UpdateCustomerCommand(7, "Ada", "555-9999", null), CancellationToken.None));
        Assert.Null(store.UpdatedValues);
    }

    [Fact]
    public async Task UpdateCustomer_RejectsSystemCustomerBeforeFieldValidation()
    {
        var store = new FakeCustomerUpdateStore(isSystem: true);
        var handler = new UpdateCustomerCommandHandler(store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new UpdateCustomerCommand(7, " ", "01712345678", null),
                CancellationToken.None));

        Assert.Equal("System customer cannot be edited", exception.Message);
        Assert.Null(store.LastPhoneCheck);
        Assert.Null(store.UpdatedValues);
    }

    [Fact]
    public async Task UpdateCustomer_ThrowsEstablishedNotFoundAndDuplicateErrors()
    {
        var missingHandler = new UpdateCustomerCommandHandler(new FakeCustomerUpdateStore(missing: true));
        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => missingHandler.Handle(
                new UpdateCustomerCommand(42, "Ada", null, null),
                CancellationToken.None));
        Assert.Equal("Customer with ID 42 not found", missing.Message);

        var owner = new CustomerReadModel(9, "Other", "01712345678", null, 0, DateTime.UnixEpoch, 0);
        var duplicateStore = new FakeCustomerUpdateStore { NormalizedPhoneOwner = owner };
        var duplicateHandler = new UpdateCustomerCommandHandler(duplicateStore);
        var duplicate = await Assert.ThrowsAsync<CustomerPhoneConflictException>(
            () => duplicateHandler.Handle(
                new UpdateCustomerCommand(7, "Ada", "01712345678", "ada@example.test"),
                CancellationToken.None));
        Assert.Equal("A customer with phone '01712345678' already exists", duplicate.Message);
        Assert.Null(duplicateStore.UpdatedValues);
    }

    private sealed class FakeCustomerUpdateStore : ICustomerUpdateStore
    {
        private readonly CustomerUpdateSnapshot? _snapshot;

        public FakeCustomerUpdateStore(
            bool isSystem = false,
            bool missing = false,
            CustomerUpdateSnapshot? snapshot = null)
        {
            _snapshot = missing
                ? null
                : snapshot ?? new CustomerUpdateSnapshot(
                    new CustomerReadModel(7, "Before", "555-0007", "before@example.test", 4, DateTime.UnixEpoch, 2),
                    isSystem);
        }

        public CustomerReadModel? NormalizedPhoneOwner { get; init; }
        public CustomerUpdateValues? UpdatedValues { get; private set; }
        public (long Id, string Phone)? LastPhoneCheck { get; private set; }
        public (long Id, string Email)? LastEmailCheck { get; private set; }

        public Task<CustomerUpdateSnapshot?> GetForUpdateAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);

        public Task<CustomerReadModel?> FindByNormalizedPhoneAsync(
            string normalizedPhone,
            long excludingCustomerId,
            CancellationToken cancellationToken)
        {
            LastPhoneCheck = (excludingCustomerId, normalizedPhone);
            return Task.FromResult(NormalizedPhoneOwner);
        }

        public Task<bool> EmailExistsAsync(
            string email,
            long excludingCustomerId,
            CancellationToken cancellationToken)
        {
            LastEmailCheck = (excludingCustomerId, email);
            return Task.FromResult(false);
        }

        public Task<CustomerReadModel> UpdateAsync(
            CustomerUpdateValues values,
            CancellationToken cancellationToken)
        {
            UpdatedValues = values;
            return Task.FromResult(new CustomerReadModel(
                values.Id,
                values.Name,
                values.Phone,
                values.Email,
                4,
                DateTime.UnixEpoch,
                2));
        }
    }
}
