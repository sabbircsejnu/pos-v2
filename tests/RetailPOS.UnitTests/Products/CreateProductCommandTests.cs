using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Products;

public sealed class CreateProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_DuplicateNameRejectsBeforeSkuAndPersistence()
    {
        var store = new FakeProductCreateStore { NameExists = true };
        var handler = new CreateProductCommandHandler(store, new FakeProductReadStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(CreateCommand("Existing Product", "SKU-1", null, false), CancellationToken.None));

        Assert.Equal("Product with name 'Existing Product' already exists", exception.Message);
        Assert.Equal(0, store.SkuLookupCount);
        Assert.Equal(0, store.CreateCount);
    }

    [Fact]
    public async Task Handle_DuplicateBarcodeRejectsBeforePersistence()
    {
        var store = new FakeProductCreateStore { BarcodeExists = true };
        var handler = new CreateProductCommandHandler(store, new FakeProductReadStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new CreateProductCommand(
                    "New Product",
                    null,
                    null,
                    "SKU-1",
                    "1234567890123",
                    1,
                    15m,
                    null,
                    0m,
                    null,
                    "active",
                    false,
                    []),
                CancellationToken.None));

        Assert.Equal("Barcode '1234567890123' already exists", exception.Message);
        Assert.Equal(0, store.CreateCount);
    }

    [Fact]
    public async Task Handle_VariantsWithoutProductCodeRejectsBeforePersistence()
    {
        var store = new FakeProductCreateStore();
        var handler = new CreateProductCommandHandler(store, new FakeProductReadStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                CreateCommand(
                    "Variant Product",
                    "SKU-2",
                    null,
                    true,
                    [new ProductVariantCreation("Blue-Small", null, null, null, 0m)]),
                CancellationToken.None));

        Assert.Equal("Main Product Code is required when creating product variants", exception.Message);
        Assert.Equal(0, store.CreateCount);
    }

    [Fact]
    public async Task Handle_MapsProductAndReturnsReadProjection()
    {
        var store = new FakeProductCreateStore();
        var expected = CreateReadModel();
        var handler = new CreateProductCommandHandler(store, new FakeProductReadStore(expected));

        var result = await handler.Handle(
            CreateCommand("New Product", null, " new-001 ", false),
            CancellationToken.None);

        Assert.Same(expected, result);
        Assert.NotNull(store.CreatedProduct);
        Assert.Equal("NEW-001", store.CreatedProduct.ProductCode);
        Assert.Equal("New Product", store.CreatedProduct.Name);
        Assert.Equal(0m, store.CreatedProduct.CostPrice);
        Assert.Equal(ProductStatus.Active, store.CreatedProduct.Status);
        Assert.StartsWith("NEW-", store.CreatedProduct.Sku);
    }

    private static CreateProductCommand CreateCommand(
        string name,
        string? sku,
        string? productCode,
        bool hasVariants,
        IReadOnlyList<ProductVariantCreation>? variants = null) =>
        new(
            name,
            null,
            productCode,
            sku,
            null,
            1,
            15m,
            null,
            0m,
            null,
            "unknown",
            hasVariants,
            variants ?? []);

    private static ProductReadModel CreateReadModel() =>
        new(
            1,
            "New Product",
            null,
            "NEW-001",
            "NEW-20260101010101",
            null,
            1,
            "Category",
            15m,
            0m,
            0m,
            null,
            false,
            null,
            null,
            "active",
            true,
            0,
            0,
            [],
            DateTime.UtcNow,
            DateTime.UtcNow);

    private sealed class FakeProductCreateStore : IProductCreateStore
    {
        public bool NameExists { get; init; }
        public bool BarcodeExists { get; init; }
        public int SkuLookupCount { get; private set; }
        public int CreateCount { get; private set; }
        public Product? CreatedProduct { get; private set; }

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(NameExists);

        public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken)
        {
            SkuLookupCount++;
            return Task.FromResult(false);
        }

        public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken) =>
            Task.FromResult(BarcodeExists);

        public Task<bool> ProductCodeExistsAsync(string productCode, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<long> CreateAsync(
            Product product,
            IReadOnlyList<ProductVariantCreation> variants,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            CreatedProduct = product;
            return Task.FromResult(1L);
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
