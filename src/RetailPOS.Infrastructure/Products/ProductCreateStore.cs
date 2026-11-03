using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductCreateStore(
    RetailPOSDbContext dbContext,
    IProductIdentifierResolver identifierResolver,
    IBusinessScope businessScope) : IProductCreateStore
{
    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AsNoTracking()
            .AnyAsync(product => product.BusinessId == businessId && product.Name.ToLower() == name.ToLower(), cancellationToken);
    }

    public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return Task.FromResult(false);
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AsNoTracking()
            .AnyAsync(product => product.BusinessId == businessId && product.Sku == sku, cancellationToken);
    }

    public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return Task.FromResult(false);
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AsNoTracking()
            .AnyAsync(product => product.BusinessId == businessId && product.Barcode == barcode, cancellationToken);
    }

    public Task<bool> ProductCodeExistsAsync(string productCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            return Task.FromResult(false);
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AsNoTracking()
            .AnyAsync(product => product.BusinessId == businessId && product.ProductCode == productCode, cancellationToken);
    }

    public async Task<long> CreateAsync(
        Product product,
        IReadOnlyList<ProductVariantCreation> variants,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();

        // The category must belong to the caller's Business; a foreign category is indistinguishable from a missing one.
        if (!await dbContext.Categories.AnyAsync(
                category => category.Id == product.CategoryId && category.BusinessId == businessId,
                cancellationToken))
            throw new KeyNotFoundException($"Category with ID {product.CategoryId} not found");

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        product.BusinessId = businessId;
        product.CreatedAt = DateTime.UtcNow;
        product.UpdatedAt = DateTime.UtcNow;
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var variantValues in variants)
        {
            var (sizeCode, attributeCode) = identifierResolver.ExtractCodesFromVariantText(
                variantValues.Name,
                variantValues.Attributes);
            var variantSku = await identifierResolver.ResolveVariantSkuAsync(
                variantValues.Sku,
                product.ProductCode!,
                sizeCode,
                attributeCode,
                cancellationToken: cancellationToken);
            var variantBarcode = await identifierResolver.ResolveVariantBarcodeAsync(
                variantValues.Barcode,
                product.CategoryId,
                cancellationToken: cancellationToken);

            dbContext.ProductVariants.Add(new ProductVariant
            {
                BusinessId = businessId,
                ProductId = product.Id,
                Name = variantValues.Name,
                Sku = variantSku,
                Barcode = variantBarcode,
                Attributes = variantValues.Attributes ?? "{}",
                PriceAdjustment = variantValues.PriceAdjustment
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return product.Id;
    }
}
