using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductSkuStore(RetailPOSDbContext dbContext, IBusinessScope businessScope) : IProductSkuStore
{
    public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Products
            .AsNoTracking()
            .AnyAsync(product => product.BusinessId == businessId && product.Sku == sku, cancellationToken);
    }
}
