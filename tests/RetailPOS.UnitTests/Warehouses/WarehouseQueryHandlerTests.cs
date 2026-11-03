using RetailPOS.Application.Warehouses;
using RetailPOS.Application.Warehouses.Abstractions;
using RetailPOS.Application.Warehouses.Queries;

namespace RetailPOS.Tests.Warehouses;

public sealed class WarehouseQueryHandlerTests
{
    [Fact]
    public async Task ListQuery_UsesTenantScopeAndSearchTerm()
    {
        var expected = new[] { CreateWarehouse(10, "Alpha") };
        var store = new FakeWarehouseReadStore { Result = expected };
        var scope = new FakeWarehouseReadScopeProvider { BusinessId = 17 };
        var handler = new GetWarehousesQueryHandler(store, scope);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(new GetWarehousesQuery("Main"), cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(17, scope.BusinessId);
        Assert.Equal(17, store.BusinessId);
        Assert.Equal("Main", store.SearchTerm);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task DetailAndStatsQueries_UseTenantScope()
    {
        var store = new FakeWarehouseReadStore
        {
            Warehouse = CreateWarehouse(10, "Alpha"),
            Stats = new WarehouseStatsReadModel(10, "Alpha", 100, 2, 0, 0)
        };
        var scope = new FakeWarehouseReadScopeProvider { BusinessId = 17 };
        var detailHandler = new GetWarehouseByIdQueryHandler(store, scope);
        var statsHandler = new GetWarehouseStatsQueryHandler(store, scope);
        using var cancellation = new CancellationTokenSource();

        Assert.Equal(store.Warehouse,
            await detailHandler.Handle(new GetWarehouseByIdQuery(10), cancellation.Token));
        Assert.Equal(store.Stats,
            await statsHandler.Handle(new GetWarehouseStatsQuery(10), cancellation.Token));
        Assert.Equal(10, store.WarehouseId);
        Assert.Equal(17, store.BusinessId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task BusinessListQuery_UsesExplicitBusinessScope()
    {
        var expected = new[] { CreateWarehouse(20, "Beta") };
        var store = new FakeWarehouseReadStore { Result = expected };
        var handler = new GetBusinessWarehousesQueryHandler(store);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(new GetBusinessWarehousesQuery(18), cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(18, store.ExplicitBusinessId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    private static WarehouseReadModel CreateWarehouse(long id, string name) =>
        new(id, name, "Address", null, null, null, 0, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private sealed class FakeWarehouseReadScopeProvider : IWarehouseReadScopeProvider
    {
        public long? BusinessId { get; init; }

        public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BusinessId);
        }
    }

    private sealed class FakeWarehouseReadStore : IWarehouseReadStore
    {
        public IReadOnlyList<WarehouseReadModel> Result { get; init; } = [];
        public WarehouseReadModel? Warehouse { get; init; }
        public WarehouseStatsReadModel? Stats { get; init; }
        public long? BusinessId { get; private set; }
        public long? ExplicitBusinessId { get; private set; }
        public long WarehouseId { get; private set; }
        public string? SearchTerm { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<WarehouseReadModel>> GetAllAsync(
            long? businessId,
            string? searchTerm,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            SearchTerm = searchTerm;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }

        public Task<WarehouseReadModel?> GetByIdAsync(
            long warehouseId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            WarehouseId = warehouseId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Warehouse);
        }

        public Task<IReadOnlyList<WarehouseReadModel>> GetByBusinessIdAsync(
            long businessId,
            CancellationToken cancellationToken)
        {
            ExplicitBusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }

        public Task<WarehouseStatsReadModel?> GetStatsAsync(
            long warehouseId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            WarehouseId = warehouseId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Stats);
        }
    }
}
