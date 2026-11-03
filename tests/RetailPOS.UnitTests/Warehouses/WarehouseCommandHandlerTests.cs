using RetailPOS.Application.Warehouses;
using RetailPOS.Application.Warehouses.Abstractions;
using RetailPOS.Application.Warehouses.Commands;

namespace RetailPOS.Tests.Warehouses;

public sealed class WarehouseCommandHandlerTests
{
    [Fact]
    public async Task CreateAndUpdateCommands_PreserveTenantScopeAndValues()
    {
        var store = new FakeWarehouseStore { Current = CreateWarehouse(10, "Alpha") };
        var scope = new FakeWarehouseReadScopeProvider { BusinessId = 17 };
        var createHandler = new CreateWarehouseCommandHandler(store, scope);
        var updateHandler = new UpdateWarehouseCommandHandler(
            store,
            store,
            scope);
        using var cancellation = new CancellationTokenSource();

        var created = await createHandler.Handle(
            new CreateWarehouseCommand("Gamma", "New Street", 0, 3),
            cancellation.Token);
        Assert.Equal(21, created.Id);
        Assert.Equal(("Gamma", "New Street", 0, 3L, 17L),
            (store.Name, store.Address, store.Capacity, store.ManagerId, store.BusinessId));
        Assert.Equal(cancellation.Token, store.CancellationToken);

        var updated = await updateHandler.Handle(
            new UpdateWarehouseCommand(10, "Alpha Updated", "Updated Street", 150, 4),
            cancellation.Token);
        Assert.Equal(10, updated.Id);
        Assert.Equal(("Alpha Updated", "Updated Street", 150, 4L, 10L, 17L),
            (store.Name, store.Address, store.Capacity, store.ManagerId, store.WarehouseId, store.BusinessId));
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task CreateCommand_RejectsDuplicateBeforeManagerLookupAndPersistence()
    {
        var store = new FakeWarehouseStore { DuplicateName = true };
        var handler = new CreateWarehouseCommandHandler(
            store,
            new FakeWarehouseReadScopeProvider { BusinessId = 17 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new CreateWarehouseCommand("Alpha", "Address", null, 99),
                CancellationToken.None));

        Assert.Equal("A warehouse with the name 'Alpha' already exists", exception.Message);
        Assert.False(store.ManagerLookupCalled);
        Assert.False(store.CreateCalled);
    }

    [Fact]
    public async Task DeleteCommand_UsesPurchaseOrderGuardAndMissingResult()
    {
        var store = new FakeWarehouseStore
        {
            Current = CreateWarehouse(10, "Alpha"),
            PurchaseOrderCount = 2
        };
        var handler = new DeleteWarehouseCommandHandler(
            store,
            store,
            new FakeWarehouseReadScopeProvider { BusinessId = 17 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteWarehouseCommand(10), CancellationToken.None));
        Assert.Equal("Cannot delete warehouse. It has 2 purchase order(s)", exception.Message);
        Assert.False(store.DeleteCalled);
        Assert.Equal(1, store.PurchaseOrderCountCalls);

        store.Current = null;
        Assert.False(await handler.Handle(new DeleteWarehouseCommand(99), CancellationToken.None));
        Assert.Equal(1, store.PurchaseOrderCountCalls);
    }

    [Fact]
    public async Task BusinessSetupCommands_UseExplicitBusinessScope()
    {
        var store = new FakeWarehouseStore
        {
            Current = CreateWarehouse(10, "Alpha"),
            PurchaseOrderCount = 0
        };
        using var cancellation = new CancellationTokenSource();

        var created = await new CreateBusinessWarehouseCommandHandler(store).Handle(
            new CreateBusinessWarehouseCommand(18, "Beta", "North Road", 200, 2),
            cancellation.Token);
        Assert.Equal(18, store.BusinessId);
        var updated = await new UpdateBusinessWarehouseCommandHandler(store, store).Handle(
            new UpdateBusinessWarehouseCommand(18, 10, "Alpha Updated", "Updated Street", 150, 2),
            cancellation.Token);
        Assert.Equal(10, updated.Id);
        Assert.Equal(18, store.BusinessId);
        var deleted = await new DeleteBusinessWarehouseCommandHandler(store, store).Handle(
            new DeleteBusinessWarehouseCommand(18, created.Id),
            cancellation.Token);
        Assert.True(deleted);
        Assert.Equal(18, store.BusinessId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    private static WarehouseReadModel CreateWarehouse(long id, string name) =>
        new(id, name, "Address", null, null, null, 0, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private sealed class FakeWarehouseReadScopeProvider : IWarehouseReadScopeProvider
    {
        public long? BusinessId { get; init; }

        public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(BusinessId);
    }

    private sealed class FakeWarehouseStore : IWarehouseReadStore, IWarehouseWriteStore
    {
        public WarehouseReadModel? Current { get; set; }
        public bool DuplicateName { get; init; }
        public int PurchaseOrderCount { get; init; }
        public bool ManagerLookupCalled { get; private set; }
        public bool CreateCalled { get; private set; }
        public bool DeleteCalled { get; private set; }
        public int PurchaseOrderCountCalls { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public string Address { get; private set; } = string.Empty;
        public int? Capacity { get; private set; }
        public long? ManagerId { get; private set; }
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
            return Task.FromResult<IReadOnlyList<WarehouseReadModel>>([]);
        }

        public Task<WarehouseReadModel?> GetByIdAsync(
            long warehouseId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            WarehouseId = warehouseId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Current);
        }

        public Task<IReadOnlyList<WarehouseReadModel>> GetByBusinessIdAsync(
            long businessId,
            CancellationToken cancellationToken)
        {
            ExplicitBusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult<IReadOnlyList<WarehouseReadModel>>([]);
        }

        public Task<WarehouseStatsReadModel?> GetStatsAsync(
            long warehouseId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            WarehouseId = warehouseId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult<WarehouseStatsReadModel?>(null);
        }

        public Task<bool> ExistsByNameAsync(
            string name,
            long? excludeWarehouseId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            Name = name;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(DuplicateName);
        }

        public Task<bool> ManagerExistsAsync(
            long managerId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            ManagerLookupCalled = true;
            ManagerId = managerId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(true);
        }

        public Task<WarehouseReadModel> CreateAsync(
            string name,
            string address,
            int? capacity,
            long? managerId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            CreateCalled = true;
            SetValues(21, name, address, capacity, managerId, businessId, cancellationToken);
            return Task.FromResult(new WarehouseReadModel(
                21, name, address, capacity, managerId, null, 0, DateTime.UnixEpoch, DateTime.UnixEpoch));
        }

        public Task<WarehouseReadModel?> UpdateAsync(
            long warehouseId,
            string name,
            string address,
            int? capacity,
            long? managerId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            SetValues(warehouseId, name, address, capacity, managerId, businessId, cancellationToken);
            return Task.FromResult<WarehouseReadModel?>(new WarehouseReadModel(
                warehouseId, name, address, capacity, managerId, null, 0, DateTime.UnixEpoch, DateTime.UnixEpoch));
        }

        public Task<int> GetPurchaseOrderCountAsync(
            long warehouseId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            PurchaseOrderCountCalls++;
            WarehouseId = warehouseId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(PurchaseOrderCount);
        }

        public Task<bool> DeleteAsync(
            long warehouseId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            WarehouseId = warehouseId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(true);
        }

        private void SetValues(
            long warehouseId,
            string name,
            string address,
            int? capacity,
            long? managerId,
            long? businessId,
            CancellationToken cancellationToken)
        {
            WarehouseId = warehouseId;
            Name = name;
            Address = address;
            Capacity = capacity;
            ManagerId = managerId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
        }
    }
}
