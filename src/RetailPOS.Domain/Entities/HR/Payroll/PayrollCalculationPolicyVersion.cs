namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollCalculationPolicyVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public int VersionNo { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string PartialMonthBasis { get; set; } = "Fixed30";
    public string SalaryBaseComponentIdsJson { get; set; } = "[]";
    public bool DeductAbsence { get; set; }
    public bool DeductUnpaidLeave { get; set; }
    public decimal? LatePerMinuteRate { get; set; }
    public decimal? EarlyLeavePerMinuteRate { get; set; }
    public string? OvertimeMode { get; set; }
    public int OvertimeDivisorDays { get; set; } = 30;
    public int StandardPaidMinutes { get; set; }
    public decimal? NormalDayOvertimeRate { get; set; }
    public decimal? WeeklyOffOvertimeRate { get; set; }
    public decimal? HolidayOvertimeRate { get; set; }
    public decimal? SpecialDayOvertimeRate { get; set; }
    public long? AbsenceComponentId { get; set; }
    public long? UnpaidLeaveComponentId { get; set; }
    public long? LateComponentId { get; set; }
    public long? EarlyLeaveComponentId { get; set; }
    public long? OvertimeComponentId { get; set; }
    public long? TaxStatutoryComponentId { get; set; }
    public string ConfigurationSnapshotJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public long CreatedByUserId { get; set; }
}
