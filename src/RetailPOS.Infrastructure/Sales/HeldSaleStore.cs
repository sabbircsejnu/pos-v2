using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

public sealed class HeldSaleStore(RetailPOSDbContext context) : IHeldSaleStore
{
    public Task<long?> GetOutletIdAsync(long id, CancellationToken cancellationToken) =>
        context.HeldSales
            .Where(heldSale => heldSale.Id == id)
            .Select(heldSale => (long?)heldSale.OutletId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<HeldSale?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        await context.HeldSales
            .Include(heldSale => heldSale.Outlet)
            .Include(heldSale => heldSale.Cashier)
            .Include(heldSale => heldSale.Customer)
            .FirstOrDefaultAsync(heldSale => heldSale.Id == id, cancellationToken);

    public async Task<IReadOnlyList<HeldSale>> GetByOutletAsync(
        long outletId,
        CancellationToken cancellationToken) =>
        await context.HeldSales
            .Include(heldSale => heldSale.Customer)
            .Include(heldSale => heldSale.Cashier)
            .Where(heldSale => heldSale.OutletId == outletId)
            .OrderByDescending(heldSale => heldSale.HeldAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<HeldSale>> GetByCashierAsync(
        long cashierId,
        IReadOnlyCollection<long> authorizedOutletIds,
        CancellationToken cancellationToken)
    {
        if (authorizedOutletIds.Count == 0)
            return [];

        return await context.HeldSales
            .Include(heldSale => heldSale.Customer)
            .Include(heldSale => heldSale.Outlet)
            .Include(heldSale => heldSale.Cashier)
            .Where(heldSale =>
                heldSale.CashierId == cashierId &&
                authorizedOutletIds.Contains(heldSale.OutletId))
            .OrderByDescending(heldSale => heldSale.HeldAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<HeldSale> CreateAsync(HeldSale heldSale, CancellationToken cancellationToken)
    {
        var outletBusinessId = await context.Outlets
            .Where(outlet => outlet.Id == heldSale.OutletId)
            .Select(outlet => outlet.BusinessId)
            .FirstOrDefaultAsync(cancellationToken);
        var cashierBusinessId = await context.Users
            .Where(user => user.Id == heldSale.CashierId)
            .Select(user => user.BusinessId)
            .FirstOrDefaultAsync(cancellationToken);
        if (outletBusinessId.HasValue && cashierBusinessId != outletBusinessId)
        {
            throw new UnauthorizedAccessException("Cashier does not belong to the authorized Outlet's Business.");
        }

        heldSale.HeldAt = DateTime.UtcNow;
        context.HeldSales.Add(heldSale);
        await context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(heldSale.Id, cancellationToken)
            ?? throw new InvalidOperationException("The held sale was saved but could not be reloaded.");
    }

    public async Task<bool> CustomerBelongsToOutletBusinessAsync(
        long customerId,
        long outletId,
        CancellationToken cancellationToken)
    {
        var outletBusinessId = await context.Outlets
            .Where(outlet => outlet.Id == outletId)
            .Select(outlet => outlet.BusinessId)
            .FirstOrDefaultAsync(cancellationToken);
        return outletBusinessId.HasValue && await context.Customers.AnyAsync(
            customer => customer.Id == customerId && customer.BusinessId == outletBusinessId.Value,
            cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var heldSale = await context.HeldSales
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (heldSale is null)
            return;

        context.HeldSales.Remove(heldSale);
        await context.SaveChangesAsync(cancellationToken);
    }
}
