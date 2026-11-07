using System.Text.Json;

namespace RetailPOS.API.DTOs.Mobile;

public sealed record MobileInventoryOutletDto(long Id, string Name);

public sealed record MobileInventoryPageDto(
    IReadOnlyList<MobileInventoryItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record MobileInventoryItemDto(
    long InventoryId,
    long ProductId,
    long VariantId,
    string ProductName,
    string? ProductCode,
    string VariantName,
    string? Sku,
    string? Barcode,
    JsonElement Attributes,
    long OutletId,
    string OutletName,
    decimal Quantity,
    int? ReorderLevel,
    bool? IsLowStock);
