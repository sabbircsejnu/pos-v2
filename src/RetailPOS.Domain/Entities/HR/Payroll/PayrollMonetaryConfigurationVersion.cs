namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollMonetaryConfigurationVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public int VersionNo { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string CurrencyCode { get; set; } = "";
    public int DecimalPrecision { get; set; }
    public string RoundingMode { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public long CreatedByUserId { get; set; }
}
