using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductUpdateStore(RetailPOSDbContext dbContext, IBusinessScope businessScope) : IProductUpdateStore
{
    public Task<ProductUpdateSnapshot?> FindForUpdateAsync(
        long productId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AsNoTracking()
            .Where(product => product.Id == productId && product.BusinessId == businessId)
            .Select(product => new ProductUpdateSnapshot(
                product.Sku,
                product.Barcode,
                product.ProductCode))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> NameExistsAsync(
        string name,
        long excludedProductId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AnyAsync(
                product => product.BusinessId == businessId
                    && product.Name.ToLower() == name.ToLower()
                    && product.Id != excludedProductId,
                cancellationToken);
    }

    public Task<bool> SkuExistsAsync(
        string sku,
        long excludedProductId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AnyAsync(
                product => product.BusinessId == businessId && product.Sku == sku && product.Id != excludedProductId,
                cancellationToken);
    }

    public Task<bool> BarcodeExistsAsync(
        string barcode,
        long excludedProductId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AnyAsync(
                product => product.BusinessId == businessId && product.Barcode == barcode && product.Id != excludedProductId,
                cancellationToken);
    }

    public Task<bool> ProductCodeExistsAsync(
        string productCode,
        long excludedProductId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AnyAsync(
                product => product.BusinessId == businessId && product.ProductCode == productCode && product.Id != excludedProductId,
                cancellationToken);
    }

    public async Task UpdateAsync(
        long productId,
        ProductUpdateValues values,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var product = await dbContext.Products
            .FirstOrDefaultAsync(product => product.Id == productId && product.BusinessId == businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Product with ID {productId} not found");

        if (!await dbContext.Categories.AnyAsync(
                category => category.Id == values.CategoryId && category.BusinessId == businessId,
                cancellationToken))
            throw new KeyNotFoundException($"Category with ID {values.CategoryId} not found");

        product.Name = values.Name;
        product.Description = values.Description;
        product.ProductCode = values.ProductCode;
        product.Sku = values.Sku;
        product.Barcode = values.Barcode;
        product.CategoryId = values.CategoryId;
        product.BasePrice = values.BasePrice;
        product.CostPrice = values.CostPrice;
        product.TaxRate = values.TaxRate;
        product.TaxClassification = values.TaxClassification;
        product.Status = values.Status;
        product.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
