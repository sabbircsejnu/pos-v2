using RetailPOS.Application.Suppliers;
using RetailPOS.Application.Suppliers.Abstractions;
using RetailPOS.Application.Suppliers.Commands;

namespace RetailPOS.Tests.Suppliers;

public sealed class SupplierCommandHandlerTests
{
    [Fact]
    public async Task CreateCommand_UsesBusinessScopeChecksNameAndForwardsCancellation()
    {
        var store = new FakeSupplierWriteStore
        {
            Created = CreateSupplier(12, "New Supplier")
        };
        var scope = new FakeSupplierReadScopeProvider { BusinessId = 17 };
        using var cancellation = new CancellationTokenSource();

        var result = await new CreateSupplierCommandHandler(store, scope).Handle(
            new CreateSupplierCommand(new SupplierWriteModel("New Supplier", "555-0100", "Address", 500)),
            cancellation.Token);

        Assert.Equal(store.Created, result);
        Assert.Equal(17, store.BusinessId);
        Assert.Equal("New Supplier", store.Name);
        Assert.Null(store.ExcludedSupplierId);
        Assert.Equal(17, store.CreatedBusinessId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task UpdateCommand_PreservesNotFoundBeforeDuplicateValidation()
    {
        var readStore = new FakeSupplierReadStore();
        var writeStore = new FakeSupplierWriteStore { NameExists = true };
        var handler = new UpdateSupplierCommandHandler(
            readStore,
            writeStore,
            new FakeSupplierReadScopeProvider { BusinessId = 17 });

        var notFound = await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(
            new UpdateSupplierCommand(99, new SupplierWriteModel("Duplicate", null, null, 0)),
            CancellationToken.None));
        Assert.Equal("Supplier with ID 99 not found", notFound.Message);
        Assert.False(writeStore.NameExistsCalled);

        readStore.Supplier = CreateSupplier(10, "Current");
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateSupplierCommand(10, new SupplierWriteModel("Duplicate", null, null, 0)),
            CancellationToken.None));
        Assert.Equal("Supplier with name 'Duplicate' already exists", duplicate.Message);
        Assert.Equal(10, writeStore.ExcludedSupplierId);
    }

    [Fact]
    public async Task DeleteCommand_PreservesBusinessScopeAndAssociationGuard()
    {
        var readStore = new FakeSupplierReadStore { Supplier = CreateSupplier(10, "Alpha") };
        var writeStore = new FakeSupplierWriteStore { PurchaseOrderCount = 1, BillCount = 2 };
        var handler = new DeleteSupplierCommandHandler(
            readStore,
            writeStore,
            new FakeSupplierReadScopeProvider { BusinessId = 17 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteSupplierCommand(10), CancellationToken.None));

        Assert.Equal(
            "Cannot delete supplier 'Alpha'. It has 1 purchase orders and 2 bills associated with it.",
            exception.Message);
        Assert.Equal(17, writeStore.BusinessId);
        Assert.False(writeStore.DeleteCalled);
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
        public SupplierReadModel? Supplier { get; set; }

        public Task<IReadOnlyList<SupplierReadModel>> GetAllAsync(
            long businessId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SupplierReadModel>>([]);

        public Task<SupplierSearchReadModel> SearchAsync(
            string? searchQuery,
            decimal? minCreditLimit,
            decimal? maxCreditLimit,
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortOrder,
            long businessId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SupplierSearchReadModel([], 0));

        public Task<SupplierReadModel?> GetByIdAsync(
            long supplierId,
            long businessId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Supplier?.Id == supplierId ? Supplier : null);

        public Task<SupplierPerformanceDataReadModel?> GetPerformanceDataAsync(
            long supplierId,
            long businessId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SupplierPerformanceDataReadModel?>(null);
    }

    private sealed class FakeSupplierWriteStore : ISupplierWriteStore
    {
        public bool NameExists { get; init; }
        public bool NameExistsCalled { get; private set; }
        public int PurchaseOrderCount { get; init; }
        public int BillCount { get; init; }
        public bool DeleteCalled { get; private set; }
        public SupplierReadModel Created { get; init; } = CreateSupplier(12, "New Supplier");
        public long BusinessId { get; private set; }
        public long CreatedBusinessId { get; private set; }
        public long? ExcludedSupplierId { get; private set; }
        public string? Name { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<bool> NameExistsAsync(
            string name,
            long businessId,
            long? excludeSupplierId,
            CancellationToken cancellationToken)
        {
            NameExistsCalled = true;
            Name = name;
            BusinessId = businessId;
            ExcludedSupplierId = excludeSupplierId;
            CancellationToken = cancellationToken;
            return Task.FromResult(NameExists);
        }

        public Task<SupplierReadModel> CreateAsync(
            SupplierWriteModel supplier,
            long businessId,
            CancellationToken cancellationToken)
        {
            CreatedBusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Created);
        }

        public Task<SupplierReadModel?> UpdateAsync(
            long supplierId,
            SupplierWriteModel supplier,
            long businessId,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult<SupplierReadModel?>(Created);
        }

        public Task<int> GetPurchaseOrderCountAsync(
            long supplierId,
            long businessId,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(PurchaseOrderCount);
        }

        public Task<int> GetBillCountAsync(
            long supplierId,
            long businessId,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(BillCount);
        }

        public Task<bool> DeleteAsync(
            long supplierId,
            long businessId,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            DeleteCalled = true;
            return Task.FromResult(true);
        }
    }
}
