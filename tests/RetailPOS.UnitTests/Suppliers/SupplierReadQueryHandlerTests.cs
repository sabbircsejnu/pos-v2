using RetailPOS.Application.Suppliers;
using RetailPOS.Application.Suppliers.Abstractions;
using RetailPOS.Application.Suppliers.Queries;

namespace RetailPOS.Tests.Suppliers;

public sealed class SupplierReadQueryHandlerTests
{
    [Fact]
    public async Task ListAndSearchQueries_UseBusinessScopeAndForwardCancellation()
    {
        var expected = new[] { CreateSupplier(10, "Alpha") };
        var store = new FakeSupplierReadStore { Suppliers = expected };
        var scope = new FakeSupplierReadScopeProvider { BusinessId = 17 };
        using var cancellation = new CancellationTokenSource();

        var list = await new GetSuppliersQueryHandler(store, scope)
            .Handle(new GetSuppliersQuery(), cancellation.Token);
        var search = await new SearchSuppliersQueryHandler(store, scope)
            .Handle(new SearchSuppliersQuery("Al", 100, 500, 2, 3, "creditlimit", "DESC"), cancellation.Token);

        Assert.Same(expected, list);
        Assert.Same(expected, search.Suppliers);
        Assert.Equal(2, search.TotalCount);
        Assert.Equal(17, store.BusinessId);
        Assert.Equal("Al", store.SearchQuery);
        Assert.Equal(100, store.MinCreditLimit);
        Assert.Equal(500, store.MaxCreditLimit);
        Assert.Equal(2, store.PageNumber);
        Assert.Equal(3, store.PageSize);
        Assert.Equal("creditlimit", store.SortBy);
        Assert.Equal("DESC", store.SortOrder);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task DetailAndPerformanceQueries_UseBusinessScopeAndPreserveNotFoundAndHealthRules()
    {
        var supplier = CreateSupplier(10, "Alpha");
        var store = new FakeSupplierReadStore
        {
            Supplier = supplier,
            PerformanceData = new SupplierPerformanceDataReadModel(10, "Alpha", 2, 3, 900, 90, 100)
        };
        var scope = new FakeSupplierReadScopeProvider { BusinessId = 17 };
        using var cancellation = new CancellationTokenSource();

        var detail = await new GetSupplierByIdQueryHandler(store, scope)
            .Handle(new GetSupplierByIdQuery(10), cancellation.Token);
        var performance = await new GetSupplierPerformanceQueryHandler(store, scope)
            .Handle(new GetSupplierPerformanceQuery(10), cancellation.Token);

        Assert.Equal(supplier, detail);
        Assert.Equal(17, store.BusinessId);
        Assert.Equal(10, store.SupplierId);
        Assert.Equal(90, performance.CreditUtilizationPercentage);
        Assert.Equal("Critical", performance.HealthStatus);
        Assert.Equal(cancellation.Token, store.CancellationToken);

        store.Supplier = null;
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new GetSupplierByIdQueryHandler(store, scope)
                .Handle(new GetSupplierByIdQuery(99), cancellation.Token));
        Assert.Equal("Supplier with ID 99 not found", exception.Message);
    }

    [Fact]
    public async Task PerformanceQuery_UsesGoodStatusWhenCreditLimitIsNotPositive()
    {
        var store = new FakeSupplierReadStore
        {
            PerformanceData = new SupplierPerformanceDataReadModel(10, "Alpha", 0, 0, 0, 50, 0)
        };
        var handler = new GetSupplierPerformanceQueryHandler(
            store,
            new FakeSupplierReadScopeProvider { BusinessId = 17 });

        var result = await handler.Handle(new GetSupplierPerformanceQuery(10), CancellationToken.None);

        Assert.Equal(0, result.CreditUtilizationPercentage);
        Assert.Equal("Good", result.HealthStatus);
    }

    private static SupplierReadModel CreateSupplier(long id, string name) =>
        new(id, name, null, null, 100, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private sealed class FakeSupplierReadScopeProvider : ISupplierReadScopeProvider
    {
        public long BusinessId { get; init; }

        public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BusinessId);
        }
    }

    private sealed class FakeSupplierReadStore : ISupplierReadStore
    {
        public IReadOnlyList<SupplierReadModel> Suppliers { get; init; } = [];
        public SupplierReadModel? Supplier { get; set; }
        public SupplierPerformanceDataReadModel? PerformanceData { get; init; }
        public long BusinessId { get; private set; }
        public long SupplierId { get; private set; }
        public string? SearchQuery { get; private set; }
        public decimal? MinCreditLimit { get; private set; }
        public decimal? MaxCreditLimit { get; private set; }
        public int PageNumber { get; private set; }
        public int PageSize { get; private set; }
        public string SortBy { get; private set; } = string.Empty;
        public string SortOrder { get; private set; } = string.Empty;
        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<SupplierReadModel>> GetAllAsync(
            long businessId,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Suppliers);
        }

        public Task<SupplierSearchReadModel> SearchAsync(
            string? searchQuery,
            decimal? minCreditLimit,
            decimal? maxCreditLimit,
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortOrder,
            long businessId,
            CancellationToken cancellationToken)
        {
            SearchQuery = searchQuery;
            MinCreditLimit = minCreditLimit;
            MaxCreditLimit = maxCreditLimit;
            PageNumber = pageNumber;
            PageSize = pageSize;
            SortBy = sortBy;
            SortOrder = sortOrder;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(new SupplierSearchReadModel(Suppliers, 2));
        }

        public Task<SupplierReadModel?> GetByIdAsync(
            long supplierId,
            long businessId,
            CancellationToken cancellationToken)
        {
            SupplierId = supplierId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Supplier?.Id == supplierId ? Supplier : null);
        }

        public Task<SupplierPerformanceDataReadModel?> GetPerformanceDataAsync(
            long supplierId,
            long businessId,
            CancellationToken cancellationToken)
        {
            SupplierId = supplierId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(PerformanceData);
        }
    }
}
