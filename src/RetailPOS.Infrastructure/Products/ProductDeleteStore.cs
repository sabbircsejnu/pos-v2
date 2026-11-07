using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Products;

public sealed class ProductDeleteStore(RetailPOSDbContext dbContext, IBusinessScope businessScope) : IProductDeleteStore
{
    public async Task DeleteAsync(long productId, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var product = await dbContext.Products
            .Include(item => item.ProductVariants)
            .FirstOrDefaultAsync(item => item.Id == productId && item.BusinessId == businessId, cancellationToken);
        if (product is null)
        {
            return;
        }

        await RetailPOS.Infrastructure.Uom.UomVariantRemoval.RemoveConfigurationAsync(
            dbContext, product.ProductVariants.Select(variant => variant.Id).ToList(), cancellationToken);
        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
