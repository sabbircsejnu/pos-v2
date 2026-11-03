using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Infrastructure.HR.Attendance;
using RetailPOS.Infrastructure.HR.Employees;
using RetailPOS.Infrastructure.HR.Leave;
using RetailPOS.Infrastructure.HR.Overtime;
using RetailPOS.Infrastructure.HR.Exit;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Approvals;

public sealed class HrApprovalKernelWorkflow(
    HrEmployeeLocationApprovalWorkflow employeeTransfers,
    HrAttendanceCorrectionApprovalWorkflow attendanceCorrections,
    HrLeaveRequestStore leaveRequests,
    HrOvertimeStore overtime,
    HrExitStore? exits = null,
    PayrollSalarySetupStore? salarySetups = null,
    PayrollSalaryRevisionStore? salaryRevisions = null,
    PayrollEmployeeLoanStore? employeeLoans = null,
    PayrollRunStore? payrollRuns = null,
    PayrollFinalSettlementStore? finalSettlements = null) : IApprovalKernelWorkflow
{
    private static readonly NoOpApprovalKernelWorkflow NoOp = new();
    public Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId, long requesterUserId, SubmitApprovalCommand request, CancellationToken ct) =>
        Workflow(request.ProcessType).PrepareSubmissionAsync(businessId, requesterUserId, request, ct);

    public Task<ApprovalDecisionResult?> FindSubmissionReplayAsync(
        long businessId, SubmitApprovalCommand request, CancellationToken ct) =>
        Workflow(request.ProcessType).FindSubmissionReplayAsync(businessId, request, ct);

    public Task SubmissionCreatedAsync(long businessId, ApprovalInstance instance, CancellationToken ct) =>
        Workflow(instance.ProcessType).SubmissionCreatedAsync(businessId, instance, ct);

    public Task DecisionCompletedAsync(
        long businessId, ApprovalInstance instance, long actorUserId, string? reason, CancellationToken ct) =>
        Workflow(instance.ProcessType).DecisionCompletedAsync(businessId, instance, actorUserId, reason, ct);

    private IApprovalKernelWorkflow Workflow(ApprovalProcessType processType) =>
        processType switch
        {
            ApprovalProcessType.EmployeeTransfer => employeeTransfers,
            ApprovalProcessType.AttendanceCorrection => attendanceCorrections,
            ApprovalProcessType.Leave => leaveRequests,
            ApprovalProcessType.Overtime => overtime,
            ApprovalProcessType.Exit => (IApprovalKernelWorkflow?)exits ?? NoOp,
            ApprovalProcessType.SalarySetup => (IApprovalKernelWorkflow?)salarySetups ?? NoOp,
            ApprovalProcessType.SalaryRevision => (IApprovalKernelWorkflow?)salaryRevisions ?? NoOp,
            ApprovalProcessType.EmployeeLoan => (IApprovalKernelWorkflow?)employeeLoans ?? NoOp,
            ApprovalProcessType.PayrollRun => (IApprovalKernelWorkflow?)payrollRuns ?? NoOp,
            ApprovalProcessType.FinalSettlement => (IApprovalKernelWorkflow?)finalSettlements ?? NoOp,
            _ => NoOp
        };
}

public sealed class HrApprovalTargetSnapshotReader(
    HrEmployeeLocationApprovalTargetSnapshotReader employeeTransfers,
    HrAttendanceCorrectionApprovalWorkflow attendanceCorrections,
    HrLeaveRequestStore leaveRequests,
    HrOvertimeStore overtime,
    HrExitStore? exits = null,
    PayrollSalarySetupStore? salarySetups = null,
    PayrollSalaryRevisionStore? salaryRevisions = null,
    PayrollEmployeeLoanStore? employeeLoans = null,
    PayrollRunStore? payrollRuns = null,
    PayrollFinalSettlementStore? finalSettlements = null) : IApprovalTargetSnapshotReader
{
    public Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken ct) =>
        targetType switch
        {
            "HrEmployeeLocationTransfer" => employeeTransfers.ReadAsync(businessId, targetType, targetId, ct),
            "HrAttendanceCorrection" => attendanceCorrections.ReadAsync(businessId, targetType, targetId, ct),
            "HrLeaveRequest" => leaveRequests.ReadAsync(businessId, targetType, targetId, ct),
            "HrOvertimeEntry" => overtime.ReadAsync(businessId, targetType, targetId, ct),
            "HrExitCase" when exits is not null => exits.ReadAsync(businessId, targetType, targetId, ct),
            PayrollSalarySetupApprovalConstants.TargetType when salarySetups is not null =>
                salarySetups.ReadAsync(businessId, targetType, targetId, ct),
            PayrollSalaryRevisionApprovalConstants.TargetType when salaryRevisions is not null =>
                salaryRevisions.ReadAsync(businessId, targetType, targetId, ct),
            PayrollEmployeeLoanApprovalConstants.TargetType when employeeLoans is not null =>
                employeeLoans.ReadAsync(businessId, targetType, targetId, ct),
            PayrollRunApprovalConstants.TargetType when payrollRuns is not null =>
                payrollRuns.ReadAsync(businessId, targetType, targetId, ct),
            PayrollFinalSettlementApprovalConstants.TargetType when finalSettlements is not null =>
                finalSettlements.ReadApprovalTargetAsync(businessId, targetType, targetId, ct),
            _ => Task.FromResult<ApprovalTargetSnapshot?>(null)
        };
}
