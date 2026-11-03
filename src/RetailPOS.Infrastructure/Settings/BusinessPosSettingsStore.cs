using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Settings;

public sealed class BusinessPosSettingsStore(
    RetailPOSDbContext dbContext,
    ISystemSettingsStore legacySettings) : IBusinessPosSettingsStore
{
    public async Task<BusinessPosSettingsSnapshot> GetAsync(long businessId, CancellationToken cancellationToken)
    {
        var row = await dbContext.BusinessPosSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.BusinessId == businessId, cancellationToken);
        return row is null
            ? await DefaultsAsync(businessId, cancellationToken)
            : ToSnapshot(row);
    }

    public async Task<(BusinessPosSettingsSnapshot Before, BusinessPosSettingsSnapshot After)> UpdateAsync(
        long businessId,
        BusinessPosSettingsUpdate update,
        long? userId,
        CancellationToken cancellationToken)
    {
        BusinessPosSettingsRules.Validate(update);

        if (!await dbContext.Businesses.AnyAsync(business => business.Id == businessId, cancellationToken))
            throw new KeyNotFoundException($"Business {businessId} was not found.");

        var row = await dbContext.BusinessPosSettings
            .SingleOrDefaultAsync(settings => settings.BusinessId == businessId, cancellationToken);
        var before = row is null
            ? await DefaultsAsync(businessId, cancellationToken)
            : ToSnapshot(row);

        if (row is null)
        {
            row = new BusinessPosSettings
            {
                BusinessId = businessId,
                MaxCashSessionDurationHours = before.MaxCashSessionDurationHours,
                DefaultTimeZoneId = before.DefaultTimeZoneId,
                TaxEnabled = before.TaxEnabled,
                DefaultTaxRate = before.DefaultTaxRate,
                TaxName = before.TaxName,
                PricesIncludeTax = before.PricesIncludeTax,
                MaxManualDiscountPercent = before.MaxManualDiscountPercent,
                AllowOwnerExpenseSelfApproval = before.AllowOwnerExpenseSelfApproval,
                Version = 0
            };
            dbContext.BusinessPosSettings.Add(row);
        }

        if (update.MaxCashSessionDurationHours.HasValue) row.MaxCashSessionDurationHours = update.MaxCashSessionDurationHours.Value;
        if (update.DefaultTimeZoneId is not null) row.DefaultTimeZoneId = update.DefaultTimeZoneId.Trim();
        if (update.TaxEnabled.HasValue) row.TaxEnabled = update.TaxEnabled.Value;
        if (update.DefaultTaxRate.HasValue) row.DefaultTaxRate = update.DefaultTaxRate.Value;
        if (update.TaxName is not null) row.TaxName = update.TaxName.Trim();
        if (update.PricesIncludeTax.HasValue) row.PricesIncludeTax = update.PricesIncludeTax.Value;
        if (update.MaxManualDiscountPercent.HasValue) row.MaxManualDiscountPercent = update.MaxManualDiscountPercent.Value;
        if (update.AllowOwnerExpenseSelfApproval.HasValue) row.AllowOwnerExpenseSelfApproval = update.AllowOwnerExpenseSelfApproval.Value;
        row.UpdatedByUserId = userId;
        row.UpdatedAt = DateTime.UtcNow;
        row.Version++;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (before, ToSnapshot(row));
    }

    private async Task<BusinessPosSettingsSnapshot> DefaultsAsync(long businessId, CancellationToken cancellationToken)
    {
        // Until a Business saves its POS settings, keep the tax behaviour it had under the legacy system settings.
        var legacy = await legacySettings.GetAsync(cancellationToken);
        return new BusinessPosSettingsSnapshot(
            businessId,
            BusinessPosSettings.MaxCashSessionDurationHoursLimit,
            BusinessPosSettings.DefaultTimeZone,
            legacy.Tax.TaxEnabled,
            legacy.Tax.DefaultTaxRate,
            string.IsNullOrWhiteSpace(legacy.Tax.TaxName) ? "VAT" : legacy.Tax.TaxName,
            legacy.Tax.PricesIncludeTax,
            0m,
            0,
            null,
            null,
            false);
    }

    private static BusinessPosSettingsSnapshot ToSnapshot(BusinessPosSettings row) =>
        new(
            row.BusinessId,
            row.MaxCashSessionDurationHours,
            row.DefaultTimeZoneId,
            row.TaxEnabled,
            row.DefaultTaxRate,
            row.TaxName,
            row.PricesIncludeTax,
            row.MaxManualDiscountPercent,
            row.Version,
            row.UpdatedAt,
            row.UpdatedByUserId,
            true,
            row.AllowOwnerExpenseSelfApproval);
}
