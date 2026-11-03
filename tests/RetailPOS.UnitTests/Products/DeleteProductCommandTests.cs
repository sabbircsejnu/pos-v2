using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Tests.Products;

public sealed class DeleteProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_RejectsProductWithStockBeforeDeleting()
    {
        var deleteStore = new FakeProductDeleteStore();
        var handler = new DeleteProductCommandHandler(
            new FakeProductReadStore(CreateReadModel(totalStock: 3)),
            deleteStore);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteProductCommand(42), CancellationToken.None));

        Assert.Equal("Cannot delete product with existing inventory (Stock: 3)", exception.Message);
        Assert.Equal(0, deleteStore.DeleteCount);
    }

    [Fact]
    public async Task Handle_MissingProductDoesNotCallDeleteStore()
    {
        var deleteStore = new FakeProductDeleteStore();
        var handler = new DeleteProductCommandHandler(new FakeProductReadStore(), deleteStore);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DeleteProductCommand(42), CancellationToken.None));

        Assert.Equal("Product with ID 42 not found", exception.Message);
        Assert.Equal(0, deleteStore.DeleteCount);
    }

    [Fact]
    public async Task Handle_DeletesProductWhenNoStockIsReported()
    {
        var deleteStore = new FakeProductDeleteStore();
        var handler = new DeleteProductCommandHandler(
            new FakeProductReadStore(CreateReadModel(totalStock: 0)),
            deleteStore);

        await handler.Handle(new DeleteProductCommand(42), CancellationToken.None);

        Assert.Equal(1, deleteStore.DeleteCount);
        Assert.Equal(42, deleteStore.DeletedProductId);
    }

    private static ProductReadModel CreateReadModel(int totalStock) =>
        new(
            42,
            "Product",
            null,
            "PRODUCT-42",
            "SKU-42",
            null,
            1,
            "Category",
            10m,
            5m,
            0m,
            null,
            false,
            null,
            null,
            "active",
            true,
            0,
            totalStock,
            [],
            DateTime.UtcNow,
            DateTime.UtcNow);

    private sealed class FakeProductDeleteStore : IProductDeleteStore
    {
        public int DeleteCount { get; private set; }
        public long? DeletedProductId { get; private set; }

        public Task DeleteAsync(long productId, CancellationToken cancellationToken)
        {
            DeleteCount++;
            DeletedProductId = productId;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProductReadStore(ProductReadModel? product = null) : IProductReadStore
    {
        public Task<ProductReadModel?> FindByIdAsync(long productId, CancellationToken cancellationToken) =>
            Task.FromResult(product);

        public Task<ProductReadModel?> FindBySkuAsync(string sku, CancellationToken cancellationToken) =>
            Task.FromResult<ProductReadModel?>(null);

        public Task<ProductReadModel?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken) =>
            Task.FromResult<ProductReadModel?>(null);

        public Task<IReadOnlyList<ProductReadModel>> ListAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductReadModel>>([]);

        public Task<IReadOnlyList<ProductReadModel>> ListByCategoryAsync(
            long categoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductReadModel>>([]);

        public Task<ProductSearchReadModel> SearchAsync(
            ProductSearchCriteria criteria,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProductSearchReadModel([], 0, criteria.PageNumber, criteria.PageSize));

        public Task<IReadOnlyList<ProductVariantSearchReadModel>> SearchVariantsAsync(
            ProductVariantSearchCriteria criteria,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductVariantSearchReadModel>>([]);
    }
}
