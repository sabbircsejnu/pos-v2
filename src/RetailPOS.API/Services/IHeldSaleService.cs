// NEW
using RetailPOS.API.DTOs.Sale;

namespace RetailPOS.API.Services;

public interface IHeldSaleService
{
    /// <summary>Park/hold the current cart so a new transaction can begin.</summary>
    Task<HeldSaleDto> HoldAsync(HoldSaleDto dto);

    /// <summary>Gets all held sales for an outlet (used to show the "parked sales" list).</summary>
    Task<HeldSaleListDto> GetByOutletAsync(long outletId);

    /// <summary>Gets all held sales by a specific cashier.</summary>
    Task<HeldSaleListDto> GetByCashierAsync(long cashierId);

    /// <summary>Retrieves a single held sale so the cart can be restored.</summary>
    Task<HeldSaleDto> GetByIdAsync(long id);

    /// <summary>
    /// Deletes the held sale record.
    /// Called after a held sale is resumed and successfully completed,
    /// or when the cashier explicitly discards it.
    /// </summary>
    Task DeleteAsync(long id);
}
