namespace RetailPOS.Core.Entities.HR.Leave;

public enum HrLeavePolicyScopeType
{
    Business,
    LocationType,
    Location,
    Employee
}

public enum HrLeaveAccrualMethod
{
    AnnualGrantAtLeaveYearStart,
    MonthlyOnJoiningDateAnniversary,
    AnnualOnJoiningDateAnniversary
}

public enum HrLeaveLedgerTransactionType
{
    Opening,
    Accrual,
    Consumption,
    Restoration,
    Adjustment,
    CarryForward,
    Expiry,
    CloseCorrection
}

public enum HrLeaveEntitlementStatus
{
    Open,
    ClosingReview,
    Closed
}

public enum HrLeaveYearLifecycleAction
{
    ClosingReview,
    Closed,
    Reopened,
    CarryForwardExpired
}

public sealed class HrLeaveType
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPaid { get; set; }
    public bool RequiresDocument { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}

public sealed class HrLeavePolicyVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LeaveTypeId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public HrLeavePolicyScopeType ScopeType { get; set; }
    public string? ScopeReference { get; set; }
    public int VersionNo { get; set; }
    public HrLeaveAccrualMethod AccrualMethod { get; set; }
    public decimal AnnualEntitlement { get; set; }
    public bool CarryForwardAllowed { get; set; }
    public decimal? CarryForwardCap { get; set; }
    public int? CarryForwardExpiryMonths { get; set; }
    public decimal? MaximumBalance { get; set; }
    public bool NegativeBalanceAllowed { get; set; }
    public string? PayrollImpactCode { get; set; }
    public int LeaveYearStartMonth { get; set; }
    public int LeaveYearStartDay { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
}

public sealed class HrLeaveEntitlement
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public long PolicyVersionId { get; set; }
    public DateOnly LeaveYearStart { get; set; }
    public DateOnly LeaveYearEnd { get; set; }
    public DateOnly JoiningDateSnapshot { get; set; }
    public long? PrimaryLocationProjectionIdSnapshot { get; set; }
    public string? LocationTypeSnapshot { get; set; }
    public HrLeaveEntitlementStatus Status { get; set; } = HrLeaveEntitlementStatus.Open;
    public int CloseVersion { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class HrLeaveLedgerEntry
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EntitlementId { get; set; }
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public long PolicyVersionId { get; set; }
    public HrLeaveLedgerTransactionType TransactionType { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string SourceType { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateOnly? ExpiresOn { get; set; }
    public long ActorRealUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class HrLeaveYearLifecycleEvent
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EntitlementId { get; set; }
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public DateOnly LeaveYearStart { get; set; }
    public int CloseVersion { get; set; }
    public HrLeaveYearLifecycleAction Action { get; set; }
    public decimal BalanceBeforeClose { get; set; }
    public decimal CarryForwardAmount { get; set; }
    public decimal ExpiredAmount { get; set; }
    public DateOnly? CarryForwardExpiresOn { get; set; }
    public long? SourceLedgerEntryId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string Reason { get; set; } = "";
    public long ActorRealUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
