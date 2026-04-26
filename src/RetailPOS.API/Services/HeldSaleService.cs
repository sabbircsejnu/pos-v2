// NEW
using System.Text.Json;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service for parking and recalling held (parked) POS sales.
/// No inventory is reserved — stock is checked only on final
/// submission via SaleService.CreateAsync.
/// </summary>
public class HeldSaleService : IHeldSaleService
{
    private readonly IHeldSaleRepository    _heldSaleRepository;
    private readonly ILogger<HeldSaleService> _logger;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public HeldSaleService(
        IHeldSaleRepository     heldSaleRepository,
        ILogger<HeldSaleService> logger)
    {
        _heldSaleRepository = heldSaleRepository;
        _logger             = logger;
    }

    public async Task<HeldSaleDto> HoldAsync(HoldSaleDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Cannot hold a sale with no items");

        var held = new HeldSale
        {
            OutletId       = dto.OutletId,
            CashierId      = dto.CashierId,
            CustomerId     = dto.CustomerId,
            ItemsJson      = JsonSerializer.Serialize(dto.Items, _json),
            DiscountPercent = dto.DiscountPercent,
            PaymentMethod  = dto.PaymentMethod.ToLower(),
            Note           = dto.Note
        };

        var created = await _heldSaleRepository.CreateAsync(held);
        _logger.LogInformation(
            "Sale held: HeldSaleId={Id} OutletId={OutletId} CashierId={CashierId}",
            created.Id, created.OutletId, created.CashierId);

        return MapToDto(created);
    }

    public async Task<HeldSaleListDto> GetByOutletAsync(long outletId)
    {
        var items = await _heldSaleRepository.GetByOutletAsync(outletId);
        var list  = items.Select(MapToDto).ToList();
        return new HeldSaleListDto { HeldSales = list, TotalCount = list.Count };
    }

    public async Task<HeldSaleListDto> GetByCashierAsync(long cashierId)
    {
        var items = await _heldSaleRepository.GetByCashierAsync(cashierId);
        var list  = items.Select(MapToDto).ToList();
        return new HeldSaleListDto { HeldSales = list, TotalCount = list.Count };
    }

    public async Task<HeldSaleDto> GetByIdAsync(long id)
    {
        var held = await _heldSaleRepository.GetByIdAsync(id);
        if (held is null)
            throw new KeyNotFoundException($"Held sale with ID {id} not found");

        return MapToDto(held);
    }

    public async Task DeleteAsync(long id)
    {
        await _heldSaleRepository.DeleteAsync(id);
        _logger.LogInformation("Held sale {Id} deleted (resumed or discarded)", id);
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private static HeldSaleDto MapToDto(HeldSale h)
    {
        var items = new List<HeldSaleItemDto>();
        try
        {
            items = JsonSerializer.Deserialize<List<HeldSaleItemDto>>(h.ItemsJson, _json)
                    ?? new List<HeldSaleItemDto>();
        }
        catch { /* corrupt JSON — return empty list */ }

        var subtotal = items.Sum(i => i.UnitPrice * i.Quantity - i.DiscountAmount);

        return new HeldSaleDto
        {
            Id             = h.Id,
            OutletId       = h.OutletId,
            OutletName     = h.Outlet?.Name ?? string.Empty,
            CashierId      = h.CashierId,
            CashierName    = h.Cashier?.Name ?? string.Empty,
            CustomerId     = h.CustomerId,
            CustomerName   = h.Customer?.Name,
            Items          = items,
            DiscountPercent = h.DiscountPercent,
            PaymentMethod  = h.PaymentMethod,
            Note           = h.Note,
            HeldAt         = h.HeldAt,
            Subtotal       = subtotal
        };
    }
}
