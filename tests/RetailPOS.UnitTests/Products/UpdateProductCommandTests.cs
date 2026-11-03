using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Tests.Products;

public sealed class UpdateProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_NormalizesFieldsAndReturnsUpdatedReadProjection()
    {
        var store = new FakeProductUpdateStore();
        var expected = CreateReadModel();
        var handler = new UpdateProductCommandHandler(store, new FakeProductReadStore(expected));

        var result = await handler.Handle(
            new UpdateProductCommand(
                9001,
                "Updated Product",
                null,
                " code-2 ",
                null,
                null,
                7001,
                125m,
                null,
                5m,
                null,
                "unknown"),
            CancellationToken.None);

        Assert.Same(expected, result);
        Assert.Equal("CODE-2", store.UpdatedValues?.ProductCode);
        Assert.Equal("ORIGINAL-SKU", store.UpdatedValues?.Sku);
        Assert.Null(store.UpdatedValues?.Barcode);
        Assert.Equal(0m, store.UpdatedValues?.CostPrice);
        Assert.Equal(RetailPOS.Core.Entities.ProductStatus.Active, store.UpdatedValues?.Status);
    }

    [Fact]
    public async Task Handle_DuplicateNameRejectsBeforeIdentifierChecksAndUpdate()
    {
        var store = new FakeProductUpdateStore { NameAlreadyExists = true };
        var handler = new UpdateProductCommandHandler(store, new FakeProductReadStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("Product with name 'Updated Product' already exists", exception.Message);
        Assert.Equal(0, store.IdentifierCheckCount);
        Assert.Null(store.UpdatedValues);
    }

    [Fact]
    public async Task Handle_MissingProductReturnsLegacyNotFoundMessage()
    {
        var store = new FakeProductUpdateStore { Snapshot = null };
        var handler = new UpdateProductCommandHandler(store, new FakeProductReadStore());

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("Product with ID 9001 not found", exception.Message);
        Assert.Equal(0, store.NameCheckCount);
    }

    private static UpdateProductCommand CreateCommand() =>
        new(9001, "Updated Product", null, null, null, null, 7001, 125m, null, 0m, null, "active");

    private static ProductReadModel CreateReadModel() =>
        new(
            9001,
            "Updated Product",
            null,
            "CODE-2",
            "ORIGINAL-SKU",
            null,
            7001,
            "Apparel",
            125m,
            0m,
            5m,
            null,
            true,
            null,
            null,
            "active",
            true,
            2,
            0,
            [],
            DateTime.UtcNow,
            DateTime.UtcNow);

    private sealed class FakeProductUpdateStore : IProductUpdateStore
    {
        public ProductUpdateSnapshot? Snapshot { get; init; } =
            new("ORIGINAL-SKU", "ORIGINAL-BARCODE", "ORIGINAL-CODE");
        public bool NameAlreadyExists { get; init; }
        public int NameCheckCount { get; private set; }
        public int IdentifierCheckCount { get; private set; }
        public ProductUpdateValues? UpdatedValues { get; private set; }

        public Task<ProductUpdateSnapshot?> FindForUpdateAsync(
            long productId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task<bool> NameExistsAsync(
            string name,
            long excludedProductId,
            CancellationToken cancellationToken)
        {
            NameCheckCount++;
            return Task.FromResult(NameAlreadyExists);
        }

        public Task<bool> SkuExistsAsync(
            string sku,
            long excludedProductId,
            CancellationToken cancellationToken)
        {
            IdentifierCheckCount++;
            return Task.FromResult(false);
        }

        public Task<bool> BarcodeExistsAsync(
            string barcode,
            long excludedProductId,
            CancellationToken cancellationToken)
        {
            IdentifierCheckCount++;
            return Task.FromResult(false);
        }

        public Task<bool> ProductCodeExistsAsync(
            string productCode,
            long excludedProductId,
            CancellationToken cancellationToken)
        {
            IdentifierCheckCount++;
            return Task.FromResult(false);
        }

        public Task UpdateAsync(
            long productId,
            ProductUpdateValues values,
            CancellationToken cancellationToken)
        {
            UpdatedValues = values;
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
