using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Settings.Commands;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Services;

public sealed class BusinessPosSettingsAuditWriter(IAuditService audit) : IBusinessPosSettingsAuditWriter
{
    public Task RecordUpdatedAsync(
        BusinessPosSettingsSnapshot before,
        BusinessPosSettingsSnapshot after,
        long? userId,
        CancellationToken cancellationToken)
    {
        var fields = new List<(string FieldName, object? OldValue, object? NewValue)>();
        void Track(string name, object? oldValue, object? newValue)
        {
            if (!Equals(oldValue, newValue))
                fields.Add((name, oldValue, newValue));
        }

        Track("MaxCashSessionDurationHours", before.MaxCashSessionDurationHours, after.MaxCashSessionDurationHours);
        Track("DefaultTimeZoneId", before.DefaultTimeZoneId, after.DefaultTimeZoneId);
        Track("TaxEnabled", before.TaxEnabled, after.TaxEnabled);
        Track("DefaultTaxRate", before.DefaultTaxRate, after.DefaultTaxRate);
        Track("TaxName", before.TaxName, after.TaxName);
        Track("PricesIncludeTax", before.PricesIncludeTax, after.PricesIncludeTax);
        Track("MaxManualDiscountPercent", before.MaxManualDiscountPercent, after.MaxManualDiscountPercent);
        Track("AllowOwnerExpenseSelfApproval", before.AllowOwnerExpenseSelfApproval, after.AllowOwnerExpenseSelfApproval);

        return audit.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Settings,
            Summary = "Business POS settings updated",
            PrimaryEntity = ("BusinessPosSettings", after.BusinessId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = after.BusinessId,
                ["actorUserId"] = userId,
                ["version"] = after.Version
            },
            Entities =
            [
                new AuditEntityInput(
                    "BusinessPosSettings",
                    after.BusinessId.ToString(),
                    before.IsPersisted ? AuditOperationType.Update : AuditOperationType.Insert,
                    fields)
            ]
        },
            cancellationToken);
    }
}
