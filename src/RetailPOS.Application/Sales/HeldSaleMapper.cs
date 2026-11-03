using System.Text.Json;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales;

internal static class HeldSaleMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static HeldSaleReadModel ToReadModel(HeldSale heldSale)
    {
        List<HeldSaleItemReadModel> items;
        try
        {
            items = JsonSerializer.Deserialize<List<HeldSaleItemReadModel>>(heldSale.ItemsJson, JsonOptions)
                ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Held sale {heldSale.Id} contains invalid cart data.",
                exception);
        }

        var subtotal = items.Sum(item => item.UnitPrice * item.Quantity - item.DiscountAmount);
        return new HeldSaleReadModel(
            heldSale.Id,
            heldSale.OutletId,
            heldSale.Outlet?.Name ?? string.Empty,
            heldSale.CashierId,
            heldSale.Cashier?.Name ?? string.Empty,
            heldSale.CustomerId,
            heldSale.Customer?.Name,
            items,
            heldSale.DiscountPercent,
            heldSale.PaymentMethod,
            heldSale.Note,
            heldSale.HeldAt,
            subtotal);
    }
}
