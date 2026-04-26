// NEW
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IHeldSaleRepository
{
    Task<HeldSale?> GetByIdAsync(long id);
    Task<IEnumerable<HeldSale>> GetByOutletAsync(long outletId);
    Task<IEnumerable<HeldSale>> GetByCashierAsync(long cashierId);
    Task<HeldSale> CreateAsync(HeldSale heldSale);
    Task DeleteAsync(long id);
}
