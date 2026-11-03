using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Application.Customers.Commands;
using RetailPOS.Application.Customers;

namespace RetailPOS.Tests.Customers;

public sealed class CreateCustomerCommandTests
{
    [Fact]
    public async Task CreateCustomer_ValidatesAndNormalizesBeforePersistence()
    {
        var store = new FakeCustomerCreateStore();
        var handler = new CreateCustomerCommandHandler(store);

        var result = await handler.Handle(
            new CreateCustomerCommand("  Ada Lovelace  ", " 01712-345678 ", " Ada@Example.Test "),
            CancellationToken.None);

        Assert.Equal(
            new CustomerCreateValues("Ada Lovelace", "01712-345678", "ada@example.test", "+8801712345678"),
            store.CreatedValues);
        Assert.Equal("Ada Lovelace", result.Name);
    }

    [Fact]
    public async Task CreateCustomer_NormalizesPhoneAndRejectsEquivalentFormatsAsConflict()
    {
        var owner = new CustomerReadModel(5, "Existing", "01712345678", null, 0, DateTime.UtcNow, 0);
        var store = new FakeCustomerCreateStore { NormalizedPhoneOwner = owner };
        var handler = new CreateCustomerCommandHandler(store);

        var conflict = await Assert.ThrowsAsync<CustomerPhoneConflictException>(
            () => handler.Handle(
                new CreateCustomerCommand("Ada", "+880 1712-345678", null),
                CancellationToken.None));

        Assert.Equal(5, conflict.Existing.Id);
        Assert.Null(store.CreatedValues);
    }

    [Fact]
    public async Task CreateCustomer_RejectsPhoneThatCannotBeNormalized()
    {
        var store = new FakeCustomerCreateStore();
        var handler = new CreateCustomerCommandHandler(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new CreateCustomerCommand("Ada", "555-0142", null), CancellationToken.None));

        Assert.False(store.PhoneWasChecked);
        Assert.Null(store.CreatedValues);
    }

    [Fact]
    public async Task CreateCustomer_StopsAtFirstDuplicateAndPreservesExceptionMessage()
    {
        var owner = new CustomerReadModel(5, "Existing", "01712345678", null, 0, DateTime.UtcNow, 0);
        var store = new FakeCustomerCreateStore { NormalizedPhoneOwner = owner };
        var handler = new CreateCustomerCommandHandler(store);

        var exception = await Assert.ThrowsAsync<CustomerPhoneConflictException>(
            () => handler.Handle(
                new CreateCustomerCommand("Ada", "01712345678", "ada@example.test"),
                CancellationToken.None));

        Assert.Equal("A customer with phone '01712345678' already exists", exception.Message);
        Assert.False(store.EmailWasChecked);
        Assert.Null(store.CreatedValues);
    }

    [Fact]
    public async Task CreateCustomer_RejectsWhitespaceNameBeforeCheckingUniqueValues()
    {
        var store = new FakeCustomerCreateStore();
        var handler = new CreateCustomerCommandHandler(store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new CreateCustomerCommand("  ", "01712345678", "ada@example.test"),
                CancellationToken.None));

        Assert.Equal("Customer name is required", exception.Message);
        Assert.False(store.PhoneWasChecked);
        Assert.False(store.EmailWasChecked);
    }

    private sealed class FakeCustomerCreateStore : ICustomerCreateStore
    {
        public bool EmailExists { get; init; }
        public CustomerReadModel? NormalizedPhoneOwner { get; init; }
        public bool PhoneWasChecked { get; private set; }
        public bool EmailWasChecked { get; private set; }
        public CustomerCreateValues? CreatedValues { get; private set; }

        public Task<CustomerReadModel?> FindByNormalizedPhoneAsync(
            string normalizedPhone,
            CancellationToken cancellationToken)
        {
            PhoneWasChecked = true;
            return Task.FromResult(NormalizedPhoneOwner);
        }

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
        {
            EmailWasChecked = true;
            return Task.FromResult(EmailExists);
        }

        public Task<CustomerReadModel> CreateAsync(
            CustomerCreateValues values,
            CancellationToken cancellationToken)
        {
            CreatedValues = values;
            return Task.FromResult(new CustomerReadModel(
                1,
                values.Name,
                values.Phone,
                values.Email,
                0,
                DateTime.UtcNow,
                0));
        }
    }
}
