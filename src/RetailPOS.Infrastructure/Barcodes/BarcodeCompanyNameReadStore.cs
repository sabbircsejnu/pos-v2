using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Barcodes;

public sealed class BarcodeCompanyNameReadStore(RetailPOSDbContext dbContext)
    : IBarcodeCompanyNameReadStore
{
    public Task<string?> FindBusinessNameAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.Businesses
            .Where(business => business.Id == businessId)
            .Select(business => business.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<long?> FindUserOutletIdAsync(
        long userId,
        CancellationToken cancellationToken) =>
        dbContext.Users
            .Where(user => user.Id == userId)
            .Select(user => user.OutletId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<string?> FindOutletNameAsync(
        long outletId,
        CancellationToken cancellationToken) =>
        dbContext.Outlets
            .Where(outlet => outlet.Id == outletId)
            .Select(outlet => outlet.Name)
            .FirstOrDefaultAsync(cancellationToken);
}
