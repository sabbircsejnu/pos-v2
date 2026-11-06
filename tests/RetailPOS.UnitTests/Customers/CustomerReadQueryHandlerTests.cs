using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Application.Customers.Queries;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerReadQueryHandlerTests
{
    [Fact]
    public async Task SearchCustomers_PreservesPagingAndSearchArguments()
    {
        var store = new FakeCustomerReadStore();
        var handler = new SearchCustomersQueryHandler(store);

        var result = await handler.Handle(
            new SearchCustomersQuery("  Ada  ", 3, 7),
            CancellationToken.None);

        Assert.Equal(("  Ada  ", 3, 7), store.LastSearch);
        Assert.Equal(3, result.PageNumber);
        Assert.Equal(7, result.PageSize);
        Assert.Equal(4, result.TotalCount);
        Assert.Same(store.Customers, result.Customers);
    }

    [Fact]
    public async Task QuickSearch_UsesTenResultLimitAndSkipsStoreForWhitespace()
    {
        var store = new FakeCustomerReadStore();
        var handler = new QuickSearchCustomersQueryHandler(store);

        var empty = await handler.Handle(new QuickSearchCustomersQuery("   "), CancellationToken.None);
        Assert.Empty(empty);
        Assert.Null(store.LastQuickSearch);

        var results = await handler.Handle(new QuickSearchCustomersQuery("Ada"), CancellationToken.None);
        Assert.Equal(("Ada", 10), store.LastQuickSearch);
        Assert.Same(store.Customers, results);
    }

    [Fact]
    public async Task PosQuickSearch_UsesTheNameOrPhoneStoreSearchWithTheSameLimitAndNeverTheEmailSearch()
    {
        var store = new FakeCustomerReadStore();
        var handler = new PosQuickSearchCustomersQueryHandler(store);

        Assert.Empty(await handler.Handle(new PosQuickSearchCustomersQuery("  "), CancellationToken.None));
        Assert.Null(store.LastPosQuickSearch);

        var results = await handler.Handle(new PosQuickSearchCustomersQuery(" 01711 "), CancellationToken.None);

        Assert.Equal(("01711", 10), store.LastPosQuickSearch);
        Assert.Null(store.LastQuickSearch);
        Assert.Same(store.Customers, results);
    }

    [Fact]
    public async Task GetCustomerById_ThrowsEstablishedNotFoundException()
    {
        var handler = new GetCustomerByIdQueryHandler(new FakeCustomerReadStore());

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(new GetCustomerByIdQuery(42), CancellationToken.None));

        Assert.Equal("Customer with ID 42 not found", exception.Message);
    }

    private sealed class FakeCustomerReadStore : ICustomerReadStore
    {
        public IReadOnlyList<CustomerReadModel> Customers { get; } =
        [
            new CustomerReadModel(1, "Ada", null, null, 0, DateTime.UnixEpoch, 2)
        ];

        public (string? Query, int PageNumber, int PageSize)? LastSearch { get; private set; }
        public (string Query, int Limit)? LastQuickSearch { get; private set; }
        public (string Query, int Limit)? LastPosQuickSearch { get; private set; }

        public Task<(IReadOnlyList<CustomerReadModel> Customers, int TotalCount)> SearchAsync(
            string? query,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken)
        {
            LastSearch = (query, pageNumber, pageSize);
            return Task.FromResult(((IReadOnlyList<CustomerReadModel>)Customers, 4));
        }

        public Task<IReadOnlyList<CustomerReadModel>> QuickSearchAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            LastQuickSearch = (query, limit);
            return Task.FromResult(Customers);
        }

        public Task<IReadOnlyList<CustomerReadModel>> QuickSearchByNameOrPhoneAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            LastPosQuickSearch = (query, limit);
            return Task.FromResult(Customers);
        }

        public Task<CustomerReadModel?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult<CustomerReadModel?>(null);
    }
}
