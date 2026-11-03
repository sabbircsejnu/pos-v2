namespace RetailPOS.API.DTOs.Settings;

/// <summary>Effective Business POS settings plus what the caller may do with the discount settings (P2.2).</summary>
public sealed record BusinessPosSettingsDto(
    int MaxCashSessionDurationHours,
    string DefaultTimeZoneId,
    bool TaxEnabled,
    decimal DefaultTaxRate,
    string TaxName,
    bool PricesIncludeTax,
    decimal MaxManualDiscountPercent,
    long Version,
    DateTime? UpdatedAt,
    bool IsPersisted,
    bool CanApplyDiscount,
    bool CanOverrideDiscountLimit,
    bool AllowOwnerExpenseSelfApproval = false);

/// <summary>Partial update: omitted members are left unchanged.</summary>
public sealed class UpdateBusinessPosSettingsDto
{
    public int? MaxCashSessionDurationHours { get; set; }
    public string? DefaultTimeZoneId { get; set; }
    public bool? TaxEnabled { get; set; }
    public decimal? DefaultTaxRate { get; set; }
    public string? TaxName { get; set; }
    public bool? PricesIncludeTax { get; set; }
    public decimal? MaxManualDiscountPercent { get; set; }

    /// <summary>D7 exception switch; changing it needs the explicit <c>expenses.self_approve</c> permission.</summary>
    public bool? AllowOwnerExpenseSelfApproval { get; set; }
}
