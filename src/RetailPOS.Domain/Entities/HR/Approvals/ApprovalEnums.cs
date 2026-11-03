namespace RetailPOS.Core.Entities.HR.Approvals;

public enum ApprovalProcessType
{
    Leave,
    Overtime,
    EmployeeTransfer,
    SalarySetup,
    SalaryRevision,
    Exit,
    PayrollRun,
    AttendanceCorrection,
    EmployeeLoan,
    FinalSettlement
}

public enum ApprovalScopeType
{
    Business,
    LocationType,
    Location
}

public enum ApprovalApproverType
{
    User,
    Role,
    ReportingManager,
    LocationManager
}

public enum ApprovalInstanceStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled
}

public enum ApprovalStepDecision
{
    Pending,
    Approved,
    Rejected,
    Skipped,
    Cancelled
}

public enum ApprovalDecisionType
{
    Approve,
    Reject,
    Cancel
}
