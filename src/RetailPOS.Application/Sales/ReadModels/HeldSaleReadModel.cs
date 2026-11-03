namespace RetailPOS.Application.Sales.ReadModels;

public sealed record HeldSaleItemReadModel
{
    public long VariantId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string VariantSku { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal DiscountAmount { get; init; }
    public string? AppliedRuleName { get; init; }
}

public sealed record HeldSaleReadModel(
    long Id,
    long OutletId,
    string OutletName,
    long CashierId,
    string CashierName,
    long? CustomerId,
    string? CustomerName,
    IReadOnlyList<HeldSaleItemReadModel> Items,
    decimal DiscountPercent,
    string PaymentMethod,
    string? Note,
    DateTime HeldAt,
    decimal Subtotal);

public sealed record HeldSaleListReadModel(
    IReadOnlyList<HeldSaleReadModel> HeldSales,
    int TotalCount);
