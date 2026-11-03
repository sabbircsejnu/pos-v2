using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Overtime;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollRunCalculationStore(RetailPOSDbContext db) : IPayrollRunCalculationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string PayrollRoundingStage = "ComponentOutput";

    public async Task<PayrollRunCalculationResponse> CalculateAsync(
        long businessId, long periodId, long runId, CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);

        var run = await db.PayrollRuns.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.PayrollPeriodId == periodId && row.Id == runId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Payroll run not found.");
        await db.Entry(run).ReloadAsync(cancellationToken);
        var period = await db.PayrollPeriods.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == periodId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        if (period.Status is not (PayrollPeriodStatuses.Open or PayrollPeriodStatuses.Processing))
            throw new PayrollRunConflictException("Payroll period is not open for calculation.");
        if (run.CurrencyCode is null || !run.DecimalPrecision.HasValue
            || run.DecimalPrecision is < 0 or > 6
            || !Enum.TryParse<MidpointRounding>(run.RoundingMode, out var roundingMode)
            || !Enum.IsDefined(roundingMode))
            throw new PayrollRunConflictException(
                "Payroll run is missing valid frozen currency and rounding settings.");
        var decimalPrecision = run.DecimalPrecision.Value;
        var selections = await db.PayrollRunEmployeeSelections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == runId && row.IsIncluded)
            .OrderBy(row => row.EmployeeCodeSnapshot).ThenBy(row => row.EmployeeId)
            .ToArrayAsync(cancellationToken);
        var employeeIds = selections.Select(row => row.EmployeeId).Distinct().ToList();
        var presentationSnapshots = await LoadEmployeePresentationSnapshotsAsync(
            businessId, period, selections, cancellationToken);
        var salaryVersionIds = selections.Select(row => row.SalaryVersionId!.Value)
            .Distinct().ToList();
        var salaryVersions = salaryVersionIds.Count == 0
            ? new Dictionary<long, PayrollEmployeeSalaryVersion>()
            : await db.PayrollEmployeeSalaryVersions.AsNoTracking()
                .Where(row => row.BusinessId == businessId && salaryVersionIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, cancellationToken);

        var adjustmentRows = employeeIds.Count == 0
            ? []
            : await db.PayrollEmployeeAdjustmentVersions.AsNoTracking()
                .Where(row => row.BusinessId == businessId && employeeIds.Contains(row.EmployeeId)
                    && row.EffectiveFrom <= period.PeriodEnd)
                .OrderBy(row => row.EmployeeId).ThenBy(row => row.AdjustmentIdentity)
                .ThenBy(row => row.EffectiveFrom).ThenBy(row => row.Version)
                .ToArrayAsync(cancellationToken);
        var loans = employeeIds.Count == 0
            ? []
            : await db.PayrollEmployeeLoans.AsNoTracking()
                .Where(row => row.BusinessId == businessId && employeeIds.Contains(row.EmployeeId))
                .OrderBy(row => row.EmployeeId).ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);
        var loanIds = loans.Select(row => row.Id).ToList();
        var scheduleVersions = loanIds.Count == 0
            ? []
            : await db.PayrollLoanScheduleVersions.AsNoTracking()
                .Where(row => row.BusinessId == businessId && loanIds.Contains(row.LoanId))
                .OrderBy(row => row.LoanId).ThenBy(row => row.VersionNo)
                .ToArrayAsync(cancellationToken);
        var scheduleVersionIds = scheduleVersions.Select(row => row.Id).ToList();
        var scheduleLines = scheduleVersionIds.Count == 0
            ? []
            : await db.PayrollLoanScheduleLines.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && scheduleVersionIds.Contains(row.ScheduleVersionId))
                .OrderBy(row => row.ScheduleVersionId).ThenBy(row => row.InstallmentNo)
                .ToArrayAsync(cancellationToken);
        var repayments = loanIds.Count == 0
            ? []
            : await db.PayrollLoanRepayments.AsNoTracking()
                .Where(row => row.BusinessId == businessId && loanIds.Contains(row.LoanId))
                .OrderBy(row => row.LoanId).ThenBy(row => row.PaymentDate).ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);
        var repaymentIds = repayments.Select(row => row.Id).ToList();
        var repaymentAllocations = repaymentIds.Count == 0
            ? []
            : await db.PayrollLoanRepaymentAllocations.AsNoTracking()
                .Where(row => row.BusinessId == businessId && repaymentIds.Contains(row.RepaymentId))
                .OrderBy(row => row.LoanId).ThenBy(row => row.ScheduleVersionId)
                .ThenBy(row => row.InstallmentNo).ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);

        var attendanceRows = employeeIds.Count == 0
            ? []
            : await db.Set<HrDailyAttendance>().AsNoTracking()
                .Where(row => row.BusinessId == businessId && employeeIds.Contains(row.EmployeeId)
                    && row.WorkDate >= period.PeriodStart && row.WorkDate <= period.PeriodEnd)
                .OrderBy(row => row.EmployeeId).ThenBy(row => row.WorkDate)
                .ToArrayAsync(cancellationToken);
        var rosterStart = new DateOnly(period.PeriodStart.Year, period.PeriodStart.Month, 1);
        var rosterEnd = new DateOnly(period.PeriodEnd.Year, period.PeriodEnd.Month,
            DateTime.DaysInMonth(period.PeriodEnd.Year, period.PeriodEnd.Month));
        var rosterRows = employeeIds.Count == 0
            ? []
            : await db.Set<HrRosterAssignment>().AsNoTracking()
                .Where(row => row.BusinessId == businessId && employeeIds.Contains(row.EmployeeId)
                    && row.Status == "Published"
                    && row.WorkDate >= rosterStart && row.WorkDate <= rosterEnd)
                .OrderBy(row => row.EmployeeId).ThenBy(row => row.WorkDate)
                .ToArrayAsync(cancellationToken);
        var leaveRequests = employeeIds.Count == 0
            ? []
            : await db.HrLeaveRequests.AsNoTracking()
                .Include(row => row.Days)
                .Where(row => row.BusinessId == businessId && employeeIds.Contains(row.EmployeeId)
                    && row.StartDate <= period.PeriodEnd && row.EndDate >= period.PeriodStart)
                .OrderBy(row => row.EmployeeId).ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);
        var leaveTypeIds = leaveRequests.Select(row => row.LeaveTypeId).Distinct().ToList();
        var leaveTypes = leaveTypeIds.Count == 0
            ? new Dictionary<long, HrLeaveType>()
            : await db.HrLeaveTypes.AsNoTracking()
                .Where(row => row.BusinessId == businessId && leaveTypeIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, cancellationToken);
        var leavePolicyIds = leaveRequests.SelectMany(row => row.Days)
            .Select(row => row.PolicyVersionId).Distinct().ToList();
        var leavePolicies = leavePolicyIds.Count == 0
            ? new Dictionary<long, HrLeavePolicyVersion>()
            : await db.HrLeavePolicyVersions.AsNoTracking()
                .Where(row => row.BusinessId == businessId && leavePolicyIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, cancellationToken);
        var overtimeRows = employeeIds.Count == 0
            ? []
            : await db.HrOvertimeEntries.AsNoTracking()
                .Include(row => row.ApprovalInstance)
                .Where(row => row.BusinessId == businessId && employeeIds.Contains(row.EmployeeId)
                    && row.WorkDate >= period.PeriodStart && row.WorkDate <= period.PeriodEnd)
                .OrderBy(row => row.EmployeeId).ThenBy(row => row.WorkDate).ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);
        var overlappingPolicies = await db.PayrollCalculationPolicyVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.EffectiveFrom <= period.PeriodEnd
                && (row.EffectiveTo == null || row.EffectiveTo >= period.PeriodStart))
            .OrderBy(row => row.EffectiveFrom).ThenBy(row => row.VersionNo)
            .ToArrayAsync(cancellationToken);
        var effectivePolicies = overlappingPolicies.Where(row =>
            row.EffectiveFrom <= period.PeriodStart
            && (row.EffectiveTo == null || row.EffectiveTo >= period.PeriodEnd)).ToArray();
        var policy = effectivePolicies.OrderByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.VersionNo).FirstOrDefault();
        var policyPeriodError = overlappingPolicies.Any(row =>
            row.EffectiveFrom > period.PeriodStart
            || row.EffectiveTo.HasValue && row.EffectiveTo.Value < period.PeriodEnd)
            || overlappingPolicies.Length > 0 && policy is null
            ? "PayrollCalculationPolicyNotEffectiveForWholePeriod"
            : null;

        var inputHash = CalculateInputHash(
            run, period, selections, salaryVersions.Values, adjustmentRows, loans,
            scheduleVersions, scheduleLines, repayments, repaymentAllocations,
            attendanceRows, rosterRows, leaveRequests, leaveTypes.Values, leavePolicies.Values,
            overtimeRows, policy);
        var existing = await db.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == runId
                && row.CalculationVersion == run.CalculationVersion)
            .ToArrayAsync(cancellationToken);
        var isLegacyCalculation = run.CalculatedAtUtc.HasValue
            && run.CalculationInputHash is null;
        if (isLegacyCalculation)
        {
            if (run.CalculationVersion != 1 || run.Status != PayrollRunStatuses.Draft
                || existing.Length != selections.Length)
                throw new PayrollRunConflictException(
                    "Legacy payroll calculation evidence cannot be safely recalculated.");
        }
        else if (run.CalculatedAtUtc.HasValue)
        {
            if (StringComparer.Ordinal.Equals(run.CalculationInputHash, inputHash))
            {
                if (existing.Length != selections.Length)
                    throw new PayrollRunConflictException(
                        "Payroll calculation evidence is incomplete and cannot be replayed.");
                return Summarize(runId, existing, isReplay: true);
            }
            if (existing.Length != selections.Length)
                throw new PayrollRunConflictException(
                    "Payroll calculation evidence is incomplete and cannot be recalculated.");
        }
        if (!run.CalculatedAtUtc.HasValue && existing.Length > 0)
            throw new PayrollRunConflictException(
                    "Payroll calculation evidence exists without its completion marker.");

        if (run.Status != PayrollRunStatuses.Draft)
            throw new PayrollRunConflictException("Only a Draft Payroll run can be calculated.");
        var now = DateTime.UtcNow;
        var results = new List<PayrollEmployeeResult>(selections.Length);
        var lines = new List<PayrollEmployeeResultLine>();
        var adjustmentHeads = adjustmentRows
            .GroupBy(row => (row.EmployeeId, row.AdjustmentIdentity))
            .Select(group => group.OrderByDescending(row => row.EffectiveFrom)
                .ThenByDescending(row => row.Version).First())
            .ToArray();
        var currentScheduleByLoan = scheduleVersions
            .Where(row => loans.Any(loan =>
                loan.Id == row.LoanId && loan.CurrentScheduleVersionNo == row.VersionNo))
            .ToDictionary(row => row.LoanId);
        var currentScheduleIds = currentScheduleByLoan.Values.Select(row => row.Id).ToArray();
        var currentLines = scheduleLines.Where(row => currentScheduleIds.Contains(row.ScheduleVersionId))
            .ToArray();
        var allocationAmounts = repaymentAllocations
            .GroupBy(row => (row.ScheduleVersionId, row.InstallmentNo))
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        var priorRunSources = await (from line in db.PayrollEmployeeResultLines.AsNoTracking()
                join result in db.PayrollEmployeeResults.AsNoTracking()
                    on new { line.BusinessId, ResultId = line.PayrollEmployeeResultId }
                    equals new { result.BusinessId, ResultId = result.Id }
                where line.BusinessId == businessId && result.PayrollRunId != runId
                    && (run.RunType != PayrollRunTypes.Correction
                        || result.PayrollRunId != run.OriginalPayrollRunId)
                    && (line.SourceType == "ManualEarning" || line.SourceType == "ManualDeduction"
                        || line.SourceType == "TaxStatutory" || line.SourceType == "LoanRecovery")
                select new PriorSource(line.SourceType, line.SourceId))
            .ToArrayAsync(cancellationToken);
        foreach (var selection in selections)
        {
            if (!salaryVersions.TryGetValue(selection.SalaryVersionId!.Value, out var salary)
                || salary.EmployeeId != selection.EmployeeId)
                throw new InvalidOperationException("Payroll eligibility references inconsistent salary evidence.");

            var employeeAdjustments = ResolveAdjustments(
                adjustmentHeads.Where(row => row.EmployeeId == selection.EmployeeId).ToArray(),
                period);
            var employeeLoans = loans.Where(row => row.EmployeeId == selection.EmployeeId
                && row.Status == "Active" && row.OutstandingAmount > 0m
                && row.RecoveryStartDate <= period.PeriodEnd).ToArray();
            var employeeAttendance = attendanceRows.Where(row => row.EmployeeId == selection.EmployeeId)
                .ToArray();
            var employeeRosters = rosterRows.Where(row => row.EmployeeId == selection.EmployeeId)
                .ToArray();
            var employeeLeave = leaveRequests.Where(row => row.EmployeeId == selection.EmployeeId
                && row.Status == "Approved").ToArray();
            var employeeOvertime = overtimeRows.Where(row => row.EmployeeId == selection.EmployeeId
                && row.Status == "PayrollEligible").ToArray();

            var result = new PayrollEmployeeResult
            {
                BusinessId = businessId,
                PayrollRunId = runId,
                CalculationVersion = checked(run.CalculationVersion + 1),
                CalculationInputHash = inputHash,
                EmployeeId = selection.EmployeeId,
                EmployeeCodeSnapshot = selection.EmployeeCodeSnapshot,
                EmployeeNameSnapshot = selection.EmployeeNameSnapshot,
                LocationIdSnapshot = selection.LocationIdSnapshot,
                SalaryVersionId = salary.Id,
                SalaryEffectiveFromSnapshot = salary.EffectiveFrom,
                CalculationPolicyVersionId = policy?.Id,
                CalculationPolicySnapshotJson = policy?.ConfigurationSnapshotJson,
                CalculatedAtUtc = now
            };
            var calculation = CalculateSalaryLines(salary.ComponentSnapshotJson, period, salary.EffectiveFrom);
            var policyCalculation = CalculatePolicyLines(
                policy, policyPeriodError, calculation.Lines, period, employeeAttendance,
                employeeRosters, employeeLeave, leaveTypes, employeeOvertime, employeeAdjustments);
            var inputException = calculation.ExceptionCode
                ?? FindInputException(
                    employeeAdjustments, employeeLoans, currentLines, currentScheduleByLoan,
                    allocationAmounts, employeeAttendance, employeeLeave, leaveTypes, leavePolicies,
                    employeeOvertime, priorRunSources, selection.EmployeeId,
                    period.PeriodStart, period.PeriodEnd)
                ?? policyCalculation.ExceptionCode;
            if (inputException is not null)
            {
                result.Status = PayrollEmployeeResultStatuses.Exception;
                result.ExceptionCode = inputException;
            }
            else
            {
                var resultLines = calculation.Lines.Select(line => new PayrollEmployeeResultLine
                {
                    BusinessId = businessId,
                    PayrollEmployeeResult = result,
                    SalaryComponentId = line.SalaryComponentId,
                    ComponentCodeSnapshot = line.Code,
                    ComponentNameSnapshot = line.Name,
                    LineType = line.LineType,
                    Amount = line.Amount,
                    SourceType = "Salary",
                    SourceId = salary.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    SourceVersion = salary.SalaryStructureVersion,
                    InputQuantity = 1m,
                    InputAmount = line.Amount,
                    CalculationOrder = line.CalculationOrder,
                    EvidenceCode = "ApprovedSalarySnapshot"
                }).ToList();
                var nextOrder = resultLines.Count == 0 ? 0 : resultLines.Max(line => line.CalculationOrder) + 1;
                foreach (var pricedLine in policyCalculation.Lines)
                {
                    pricedLine.BusinessId = businessId;
                    pricedLine.PayrollEmployeeResult = result;
                    pricedLine.CalculationOrder = nextOrder++;
                    resultLines.Add(pricedLine);
                }
                foreach (var adjustment in employeeAdjustments)
                {
                    if (!adjustment.IsApplicable)
                        continue;
                    var sourceType = adjustment.Row.Classification == "Statutory"
                        ? "TaxStatutory"
                        : adjustment.Row.AdjustmentKind switch
                        {
                            "RecurringAllowance" => "RecurringAllowance",
                            "RecurringDeduction" => "RecurringDeduction",
                            "OneTimeEarning" => "ManualEarning",
                            "OneTimeDeduction" => "ManualDeduction",
                            _ => throw new InvalidOperationException(
                                "Payroll adjustment has an unsupported adjustment kind.")
                        };
                    resultLines.Add(new PayrollEmployeeResultLine
                    {
                        BusinessId = businessId,
                        PayrollEmployeeResult = result,
                        SalaryComponentId = adjustment.Row.SalaryComponentId,
                        ComponentCodeSnapshot = adjustment.Row.ComponentCodeSnapshot,
                        ComponentNameSnapshot = adjustment.Row.ComponentNameSnapshot,
                        LineType = adjustment.Row.ComponentTypeSnapshot,
                        Amount = adjustment.Row.Amount,
                        SourceType = sourceType,
                        SourceId = AdjustmentSourceId(adjustment.Row),
                        SourceVersion = adjustment.Row.Version,
                        InputAmount = adjustment.Row.Amount,
                        CalculationOrder = nextOrder++,
                        EvidenceCode = adjustment.Row.Classification
                    });
                }
                foreach (var loan in employeeLoans)
                {
                    if (!currentScheduleByLoan.TryGetValue(loan.Id, out var schedule))
                        continue;
                    var remainingOutstanding = loan.OutstandingAmount;
                    var dueLines = currentLines.Where(row => row.ScheduleVersionId == schedule.Id
                            && row.DueDate >= period.PeriodStart && row.DueDate <= period.PeriodEnd)
                        .OrderBy(row => row.DueDate).ThenBy(row => row.InstallmentNo).ToArray();
                    foreach (var installment in dueLines)
                    {
                        var allocated = allocationAmounts.GetValueOrDefault(
                            (schedule.Id, installment.InstallmentNo));
                        var proposed = Math.Max(0m, installment.PlannedAmount - allocated);
                        var recovery = Math.Min(proposed, remainingOutstanding);
                        if (recovery <= 0m)
                            continue;
                        resultLines.Add(new PayrollEmployeeResultLine
                        {
                            BusinessId = businessId,
                            PayrollEmployeeResult = result,
                            ComponentCodeSnapshot = loan.LoanType,
                            ComponentNameSnapshot = loan.LoanType,
                            LineType = "Deduction",
                            Amount = recovery,
                            SourceType = "LoanRecovery",
                            SourceId = $"{loan.Id}:{schedule.VersionNo}:{installment.InstallmentNo}",
                            SourceVersion = schedule.VersionNo,
                            InputQuantity = 1m,
                            InputAmount = proposed,
                            CalculationOrder = nextOrder++,
                            EvidenceCode = "DueLoanInstallment"
                        });
                        remainingOutstanding -= recovery;
                    }
                }
                foreach (var line in resultLines)
                    line.Amount = RoundMoney(line.Amount, decimalPrecision, roundingMode);
                result.GrossEarnings = resultLines
                    .Where(line => line.LineType == "Earning").Sum(line => line.Amount);
                result.TotalDeductions = resultLines
                    .Where(line => line.LineType == "Deduction").Sum(line => line.Amount);
                result.NetPay = result.GrossEarnings.Value - result.TotalDeductions.Value;
                lines.AddRange(resultLines);
            }
            var snapshotJson = BuildEmployeeSnapshot(
                period, run, selection, salary, policy, employeeAdjustments, employeeLoans,
                scheduleVersions, scheduleLines, repayments, repaymentAllocations,
                employeeAttendance, employeeRosters, employeeLeave, leaveTypes, leavePolicies,
                employeeOvertime, presentationSnapshots.GetValueOrDefault(selection.EmployeeId),
                result, resultsLines: lines
                    .Where(line => ReferenceEquals(line.PayrollEmployeeResult, result))
                    .ToArray());
            result.CalculationSnapshotJson = snapshotJson;
            result.CalculationSnapshotHash = HashCanonicalJson(snapshotJson);
            results.Add(result);
        }

        db.PayrollEmployeeResults.AddRange(results);
        db.PayrollEmployeeResultLines.AddRange(lines);
        run.CalculatedAtUtc = now;
        run.CalculationVersion = checked(run.CalculationVersion + 1);
        run.CalculationInputHash = inputHash;
        run.Version = checked(run.Version + 1);
        await SaveAsync(cancellationToken);
        return Summarize(runId, results, isReplay: false);
    }

    private static string CalculateInputHash(
        PayrollRun run,
        PayrollPeriod period,
        IReadOnlyCollection<PayrollRunEmployeeSelection> selections,
        IEnumerable<PayrollEmployeeSalaryVersion> salaries,
        IReadOnlyCollection<PayrollEmployeeAdjustmentVersion> adjustments,
        IReadOnlyCollection<PayrollEmployeeLoan> loans,
        IReadOnlyCollection<PayrollLoanScheduleVersion> scheduleVersions,
        IReadOnlyCollection<PayrollLoanScheduleLine> scheduleLines,
        IReadOnlyCollection<PayrollLoanRepayment> repayments,
        IReadOnlyCollection<PayrollLoanRepaymentAllocation> allocations,
        IReadOnlyCollection<HrDailyAttendance> attendance,
        IReadOnlyCollection<HrRosterAssignment> rosters,
        IReadOnlyCollection<HrLeaveRequest> leaveRequests,
        IEnumerable<HrLeaveType> leaveTypes,
        IEnumerable<HrLeavePolicyVersion> leavePolicies,
        IReadOnlyCollection<HrOvertimeEntry> overtime,
        PayrollCalculationPolicyVersion? policy)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            PayrollRoundingStage,
            Currency = new
            {
                run.MonetaryConfigurationVersionId,
                run.CurrencyCode,
                run.DecimalPrecision,
                run.RoundingMode
            },
            Period = new
            {
                period.BusinessId,
                period.Id,
                period.PeriodCode,
                period.PeriodStart,
                period.PeriodEnd,
                period.PayDate,
                period.Status,
                period.Version
            },
            Selections = selections.OrderBy(row => row.EmployeeId).Select(row => new
            {
                row.EmployeeId,
                row.EligibilityDate,
                row.SalaryVersionId,
                row.SalaryEffectiveFromSnapshot,
                row.LocationIdSnapshot,
                row.EmploymentPeriodId,
                row.EmploymentStartDateSnapshot,
                row.EmploymentEndDateSnapshot
            }),
            Salaries = salaries.OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.EmployeeId,
                row.EffectiveFrom,
                row.SalaryStructureId,
                row.SalaryStructureVersion,
                row.StructureSnapshotJson,
                row.ComponentSnapshotJson,
                row.ApprovalInstanceId
            }),
            Adjustments = adjustments.OrderBy(row => row.EmployeeId)
                .ThenBy(row => row.AdjustmentIdentity).ThenBy(row => row.Version).Select(row => new
                {
                    row.Id, row.EmployeeId, row.AdjustmentIdentity, row.Version,
                    row.SalaryComponentId, row.ComponentCodeSnapshot, row.ComponentNameSnapshot,
                    row.ComponentTypeSnapshot, row.ValueTypeSnapshot, row.AdjustmentKind,
                    row.Classification, row.Amount, row.EffectiveFrom, row.EffectiveTo, row.Status
                }),
            Loans = loans.OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.EmployeeId, row.LoanType, row.OutstandingAmount, row.RecoveryStartDate,
                row.RecoveryMode, row.FixedInstallment, row.CurrentScheduleVersionNo, row.Status, row.Version
            }),
            Schedules = scheduleVersions.OrderBy(row => row.LoanId).ThenBy(row => row.VersionNo)
                .Select(row => new
                {
                    row.Id, row.LoanId, row.VersionNo, row.RecoveryStartDate, row.RecoveryMode,
                    row.FixedInstallment
                }),
            ScheduleLines = scheduleLines.OrderBy(row => row.ScheduleVersionId)
                .ThenBy(row => row.InstallmentNo).Select(row => new
                {
                    row.Id, row.ScheduleVersionId, row.InstallmentNo, row.DueDate, row.PlannedAmount
                }),
            Repayments = repayments.OrderBy(row => row.LoanId).ThenBy(row => row.PaymentDate)
                .ThenBy(row => row.Id).Select(row => new
                {
                    row.Id, row.LoanId, row.Amount, row.PaymentDate, row.IdempotencyPayloadHash
                }),
            Allocations = allocations.OrderBy(row => row.RepaymentId).ThenBy(row => row.Id)
                .Select(row => new
                {
                    row.Id, row.LoanId, row.RepaymentId, row.ScheduleVersionId,
                    row.InstallmentNo, row.Amount
                }),
            Attendance = attendance.OrderBy(row => row.EmployeeId).ThenBy(row => row.WorkDate)
                .Select(row => new
                {
                    row.Id, row.EmployeeId, row.WorkDate, row.Status, row.WorkedMinutes,
                    row.LateMinutes, row.EarlyLeaveMinutes, row.OvertimeMinutesCalculated,
                    row.ProcessingVersion, row.IsManuallyAdjusted, row.CalculationFingerprint,
                    row.ShiftSnapshotJson, row.LocationSnapshotJson, row.CalendarEvidenceJson
                }),
            Rosters = rosters.OrderBy(row => row.EmployeeId).ThenBy(row => row.WorkDate)
                .Select(row => new
                {
                    row.Id, row.EmployeeId, row.WorkDate, row.WorkKind, row.DayClassification,
                    row.Status, row.Version, row.ShiftSnapshotJson, row.LocationSnapshotJson,
                    row.CalendarEvidenceJson
                }),
            LeaveRequests = leaveRequests.OrderBy(row => row.EmployeeId).ThenBy(row => row.Id)
                .Select(row => new
                {
                    row.Id, row.EmployeeId, row.LeaveTypeId, row.Status, row.ApprovalInstanceId,
                    row.Version,
                    Days = row.Days.OrderBy(day => day.WorkDate).ThenBy(day => day.Id).Select(day => new
                    {
                        day.Id, day.EmployeeId, day.WorkDate, day.Units, day.DayClassification,
                        day.PolicyVersionId, day.ShiftSnapshotJson, day.CalendarEvidenceJson
                    })
                }),
            LeaveTypes = leaveTypes.OrderBy(row => row.Id).Select(row => new { row.Id, row.IsPaid }),
            LeavePolicies = leavePolicies.OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.PayrollImpactCode }),
            Overtime = overtime.OrderBy(row => row.EmployeeId).ThenBy(row => row.WorkDate)
                .ThenBy(row => row.Id).Select(row => new
                {
                    row.Id, row.EmployeeId, row.WorkDate, row.Status, row.ApprovedMinutes,
                    row.CalculatedMinutes, row.CalculationVersion, row.AttendanceProcessingVersion,
                    row.PolicyVersionId, row.PolicyVersionNo, row.PolicySnapshotJson,
                    row.CalculationInputsJson, row.CalculationFingerprint,
                    ApprovalStatus = row.ApprovalInstance?.Status.ToString()
                }),
            CalculationPolicy = policy is null ? null : new
            {
                policy.Id,
                policy.VersionNo,
                policy.EffectiveFrom,
                policy.EffectiveTo,
                policy.ConfigurationSnapshotJson
            }
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string BuildEmployeeSnapshot(
        PayrollPeriod period,
        PayrollRun run,
        PayrollRunEmployeeSelection selection,
        PayrollEmployeeSalaryVersion salary,
        PayrollCalculationPolicyVersion? policy,
        IReadOnlyCollection<AdjustmentResolution> adjustments,
        IReadOnlyCollection<PayrollEmployeeLoan> loans,
        IReadOnlyCollection<PayrollLoanScheduleVersion> scheduleVersions,
        IReadOnlyCollection<PayrollLoanScheduleLine> scheduleLines,
        IReadOnlyCollection<PayrollLoanRepayment> repayments,
        IReadOnlyCollection<PayrollLoanRepaymentAllocation> allocations,
        IReadOnlyCollection<HrDailyAttendance> attendance,
        IReadOnlyCollection<HrRosterAssignment> rosters,
        IReadOnlyCollection<HrLeaveRequest> leaveRequests,
        IReadOnlyDictionary<long, HrLeaveType> leaveTypes,
        IReadOnlyDictionary<long, HrLeavePolicyVersion> leavePolicies,
        IReadOnlyCollection<HrOvertimeEntry> overtime,
        PayrollEmployeePresentationSnapshot? presentation,
        PayrollEmployeeResult result,
        IReadOnlyCollection<PayrollEmployeeResultLine> resultsLines)
    {
        var loanIds = loans.Select(row => row.Id).ToHashSet();
        var scheduleIds = scheduleVersions.Where(row => loanIds.Contains(row.LoanId))
            .Select(row => row.Id).ToHashSet();
        var repaymentIds = repayments.Where(row => loanIds.Contains(row.LoanId))
            .Select(row => row.Id).ToHashSet();
        var snapshot = new
        {
            SchemaVersion = 1,
            PayrollRoundingStage,
            Period = new
            {
                period.PeriodCode,
                period.PeriodStart,
                period.PeriodEnd,
                period.PayDate
            },
            Currency = new
            {
                run.MonetaryConfigurationVersionId,
                run.CurrencyCode,
                run.DecimalPrecision,
                run.RoundingMode
            },
            Employee = new
            {
                selection.EmployeeId,
                selection.EmployeeCodeSnapshot,
                selection.EmployeeNameSnapshot,
                selection.LocationIdSnapshot,
                PositionId = presentation?.PositionId,
                PositionCode = presentation?.PositionCode,
                PositionName = presentation?.PositionName,
                LocationName = presentation?.LocationName,
                LocationReference = presentation?.LocationReference,
                LocationType = presentation?.LocationType,
                selection.EligibilityDate
            },
            Salary = new
            {
                salary.Id,
                salary.EffectiveFrom,
                salary.SalaryStructureId,
                salary.SalaryStructureVersion,
                salary.StructureSnapshotJson,
                salary.ComponentSnapshotJson,
                salary.ApprovalInstanceId
            },
            CalculationPolicy = policy is null ? null : new
            {
                policy.Id,
                policy.VersionNo,
                policy.EffectiveFrom,
                policy.EffectiveTo,
                policy.ConfigurationSnapshotJson
            },
            Adjustments = adjustments.OrderBy(row => row.Row.AdjustmentIdentity)
                .ThenBy(row => row.Row.Version).Select(row => new
                {
                    row.Row.Id,
                    row.Row.AdjustmentIdentity,
                    row.Row.Version,
                    row.Row.SalaryComponentId,
                    row.Row.ComponentCodeSnapshot,
                    row.Row.ComponentNameSnapshot,
                    row.Row.ComponentTypeSnapshot,
                    row.Row.ValueTypeSnapshot,
                    row.Row.AdjustmentKind,
                    row.Row.Classification,
                    row.Row.Amount,
                    row.Row.EffectiveFrom,
                    row.Row.EffectiveTo,
                    row.Row.Status,
                    row.IsApplicable
                }),
            Loans = loans.OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.LoanType,
                row.OutstandingAmount,
                row.RecoveryStartDate,
                row.RecoveryMode,
                row.FixedInstallment,
                row.CurrentScheduleVersionNo,
                row.Status,
                row.Version
            }),
            LoanSchedules = scheduleVersions.Where(row => loanIds.Contains(row.LoanId))
                .OrderBy(row => row.LoanId).ThenBy(row => row.VersionNo).Select(row => new
                {
                    row.Id,
                    row.LoanId,
                    row.VersionNo,
                    row.RecoveryStartDate,
                    row.RecoveryMode,
                    row.FixedInstallment
                }),
            LoanScheduleLines = scheduleLines.Where(row => scheduleIds.Contains(row.ScheduleVersionId))
                .OrderBy(row => row.ScheduleVersionId).ThenBy(row => row.InstallmentNo)
                .Select(row => new
                {
                    row.Id,
                    row.ScheduleVersionId,
                    row.InstallmentNo,
                    row.DueDate,
                    row.PlannedAmount
                }),
            LoanRepayments = repayments.Where(row => loanIds.Contains(row.LoanId))
                .OrderBy(row => row.LoanId).ThenBy(row => row.PaymentDate).ThenBy(row => row.Id)
                .Select(row => new { row.Id, row.LoanId, row.Amount, row.PaymentDate }),
            LoanRepaymentAllocations = allocations.Where(row =>
                    repaymentIds.Contains(row.RepaymentId) || scheduleIds.Contains(row.ScheduleVersionId))
                .OrderBy(row => row.LoanId).ThenBy(row => row.ScheduleVersionId)
                .ThenBy(row => row.InstallmentNo).ThenBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.LoanId,
                    row.RepaymentId,
                    row.ScheduleVersionId,
                    row.InstallmentNo,
                    row.Amount
                }),
            Attendance = attendance.OrderBy(row => row.WorkDate).Select(row => new
            {
                row.WorkDate,
                row.Status,
                row.WorkedMinutes,
                row.LateMinutes,
                row.EarlyLeaveMinutes,
                row.OvertimeMinutesCalculated,
                row.ProcessingVersion,
                row.IsManuallyAdjusted,
                row.CalculationFingerprint,
                row.ShiftSnapshotJson,
                row.LocationSnapshotJson,
                row.CalendarEvidenceJson
            }),
            Rosters = rosters.OrderBy(row => row.WorkDate).Select(row => new
            {
                row.WorkDate,
                row.WorkKind,
                row.DayClassification,
                row.Status,
                row.Version,
                row.ShiftSnapshotJson,
                row.LocationSnapshotJson,
                row.CalendarEvidenceJson
            }),
            ApprovedLeave = leaveRequests.OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.LeaveTypeId,
                row.Status,
                row.ApprovalInstanceId,
                Days = row.Days.OrderBy(day => day.WorkDate).ThenBy(day => day.Id).Select(day => new
                {
                    day.WorkDate,
                    day.Units,
                    day.DayClassification,
                    day.PolicyVersionId,
                    day.ShiftSnapshotJson,
                    day.CalendarEvidenceJson,
                    LeaveTypePaid = leaveTypes.GetValueOrDefault(row.LeaveTypeId)?.IsPaid,
                    LeavePolicyImpact = leavePolicies.GetValueOrDefault(day.PolicyVersionId)?.PayrollImpactCode
                })
            }),
            Overtime = overtime.OrderBy(row => row.WorkDate).ThenBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.WorkDate,
                row.Status,
                row.ApprovedMinutes,
                row.CalculatedMinutes,
                row.CalculationVersion,
                row.AttendanceProcessingVersion,
                row.PolicyVersionId,
                row.PolicyVersionNo,
                row.PolicySnapshotJson,
                row.CalculationInputsJson,
                row.CalculationFingerprint,
                ApprovalStatus = row.ApprovalInstance?.Status.ToString()
            }),
            Outcome = new
            {
                result.Status,
                result.ExceptionCode,
                result.GrossEarnings,
                result.TotalDeductions,
                result.NetPay
            },
            Lines = resultsLines.OrderBy(row => row.CalculationOrder).Select(row => new
            {
                row.SalaryComponentId,
                row.ComponentCodeSnapshot,
                row.ComponentNameSnapshot,
                row.LineType,
                row.Amount,
                row.SourceType,
                row.SourceId,
                row.SourceVersion,
                row.InputQuantity,
                row.InputRate,
                row.InputAmount,
                row.EvidenceCode,
                row.CalculationOrder
            })
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private async Task<Dictionary<long, PayrollEmployeePresentationSnapshot>>
        LoadEmployeePresentationSnapshotsAsync(
            long businessId,
            PayrollPeriod period,
            IReadOnlyCollection<PayrollRunEmployeeSelection> selections,
            CancellationToken cancellationToken)
    {
        if (selections.Count == 0)
            return [];

        var employeeIds = selections.Select(row => row.EmployeeId).Distinct().ToList();
        var assignments = await db.HrEmployeePositionAssignments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && employeeIds.Contains(row.EmployeeId)
                && row.AssignmentType == HrEmployeeAssignmentType.Primary
                && row.EffectiveFrom <= period.PeriodEnd
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= period.PeriodStart))
            .OrderBy(row => row.EmployeeId).ThenByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var positionIds = assignments.Select(row => row.PositionId).Distinct().ToList();
        var positions = positionIds.Count == 0
            ? []
            : await db.HrPositions.AsNoTracking()
                .Where(row => row.BusinessId == businessId && positionIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, cancellationToken);
        var locationIds = selections.Where(row => row.LocationIdSnapshot.HasValue)
            .Select(row => row.LocationIdSnapshot!.Value).Distinct().ToList();
        var locations = locationIds.Count == 0
            ? []
            : await db.OperationalLocationProjections.AsNoTracking()
                .Where(row => row.BusinessId == businessId && locationIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, cancellationToken);

        var result = new Dictionary<long, PayrollEmployeePresentationSnapshot>();
        foreach (var selection in selections)
        {
            var effectivePosition = assignments.Where(row => row.EmployeeId == selection.EmployeeId
                    && row.EffectiveFrom <= selection.EligibilityDate
                    && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= selection.EligibilityDate))
                .Select(row => positions.GetValueOrDefault(row.PositionId))
                .Where(row => row is not null
                    && row.EffectiveFrom <= selection.EligibilityDate
                    && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= selection.EligibilityDate))
                .OrderByDescending(row => row!.EffectiveFrom).ThenByDescending(row => row!.Id)
                .FirstOrDefault();
            var location = selection.LocationIdSnapshot is { } locationId
                ? locations.GetValueOrDefault(locationId)
                : null;
            result[selection.EmployeeId] = new PayrollEmployeePresentationSnapshot(
                effectivePosition?.Id,
                effectivePosition?.Code,
                effectivePosition?.Name,
                location?.Name,
                location?.SourceEntityId,
                location?.SourceEntityType);
        }
        return result;
    }

    private static string HashCanonicalJson(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static string AdjustmentSourceId(PayrollEmployeeAdjustmentVersion row) =>
        FormattableString.Invariant($"{row.EmployeeId}:{row.AdjustmentIdentity:N}:{row.Version}");

    private static AdjustmentResolution[] ResolveAdjustments(
        IReadOnlyCollection<PayrollEmployeeAdjustmentVersion> rows,
        PayrollPeriod period)
    {
        var result = new List<AdjustmentResolution>();
        foreach (var row in rows.Where(row => row.Status == "Active"))
        {
            if (row.EffectiveTo < period.PeriodStart)
                continue;
            if (row.AdjustmentKind is "OneTimeEarning" or "OneTimeDeduction")
            {
                if (row.EffectiveFrom >= period.PeriodStart && row.EffectiveFrom <= period.PeriodEnd)
                    result.Add(new AdjustmentResolution(row, true, null));
                continue;
            }

            if (row.AdjustmentKind is not ("RecurringAllowance" or "RecurringDeduction"))
            {
                result.Add(new AdjustmentResolution(row, false, "InvalidAdjustmentKind"));
                continue;
            }
            if (row.EffectiveFrom > period.PeriodEnd)
                continue;
            var overlaps = row.EffectiveFrom <= period.PeriodEnd
                && (row.EffectiveTo is null || row.EffectiveTo >= period.PeriodStart);
            if (!overlaps)
                continue;
            var coversWholePeriod = row.EffectiveFrom <= period.PeriodStart
                && (row.EffectiveTo is null || row.EffectiveTo >= period.PeriodEnd);
            result.Add(new AdjustmentResolution(row, coversWholePeriod,
                coversWholePeriod ? null : "PartialAdjustmentPeriodPolicyNotConfigured"));
        }
        return result.ToArray();
    }

    private static string? FindInputException(
        IReadOnlyCollection<AdjustmentResolution> adjustments,
        IReadOnlyCollection<PayrollEmployeeLoan> loans,
        IReadOnlyCollection<PayrollLoanScheduleLine> scheduleLines,
        IReadOnlyDictionary<long, PayrollLoanScheduleVersion> currentScheduleByLoan,
        IReadOnlyDictionary<(long ScheduleVersionId, int InstallmentNo), decimal> allocationAmounts,
        IReadOnlyCollection<HrDailyAttendance> attendance,
        IReadOnlyCollection<HrLeaveRequest> leaveRequests,
        IReadOnlyDictionary<long, HrLeaveType> leaveTypes,
        IReadOnlyDictionary<long, HrLeavePolicyVersion> leavePolicies,
        IReadOnlyCollection<HrOvertimeEntry> overtime,
        IReadOnlyCollection<PriorSource> priorRunSources,
        long employeeId,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var adjustmentError = adjustments.FirstOrDefault(row => row.ExceptionCode is not null);
        if (adjustmentError is not null)
            return adjustmentError.ExceptionCode;

        foreach (var adjustment in adjustments.Where(row => row.IsApplicable))
        {
            if (adjustment.Row.Amount < 0m
                || adjustment.Row.ComponentTypeSnapshot is not ("Earning" or "Deduction"))
                return "InvalidAdjustmentInput";
            if (adjustment.Row.ComponentTypeSnapshot
                != (adjustment.Row.AdjustmentKind is "RecurringAllowance" or "OneTimeEarning"
                    ? "Earning" : "Deduction"))
                return "AdjustmentComponentTypeMismatch";
            if (adjustment.Row.Classification == "Statutory"
                && adjustment.Row.ComponentTypeSnapshot == "Earning")
                return "InvalidStatutoryAdjustment";

            var sourceType = adjustment.Row.AdjustmentKind switch
            {
                "OneTimeEarning" or "OneTimeDeduction" =>
                    adjustment.Row.Classification == "Statutory"
                        ? "TaxStatutory"
                        : adjustment.Row.AdjustmentKind == "OneTimeEarning"
                            ? "ManualEarning"
                            : "ManualDeduction",
                _ => null
            };
            var sourceId = AdjustmentSourceId(adjustment.Row);
            if (sourceType is not null && priorRunSources.Any(source =>
                    source.SourceType == sourceType && source.SourceId == sourceId))
                return "OneTimeAdjustmentAlreadyConsumed";
        }

        var eligibleLeaveDays = leaveRequests.SelectMany(request => request.Days
                .Where(day => day.EmployeeId == employeeId
                    && day.WorkDate >= periodStart && day.WorkDate <= periodEnd)
                .Select(day => new { Request = request, Day = day }))
            .Where(row => row.Request.Status == "Approved")
            .ToArray();
        if (eligibleLeaveDays.GroupBy(row => row.Day.WorkDate).Any(group => group.Count() > 1))
            return "ConflictingApprovedLeaveDays";

        var approvedLeaveDates = eligibleLeaveDays.Select(row => row.Day.WorkDate).ToHashSet();

        foreach (var row in attendance.Where(row => row.WorkDate >= periodStart && row.WorkDate <= periodEnd))
        {
            if (approvedLeaveDates.Contains(row.WorkDate))
                continue;
            if (row.Status == "MissingPunch")
                return "IncompleteAttendanceEvidence";
            if (row.Status == "Leave")
                return "AttendanceLeaveEvidenceNotApproved";
        }

        if (overtime.Any(row => row.ApprovedMinutes is null or <= 0
            || row.ApprovalInstance?.Status != ApprovalInstanceStatus.Approved))
            return "IncompleteOvertimeApprovalEvidence";

        foreach (var loan in loans)
        {
            if (loan.OutstandingAmount < 0m)
                return "InvalidLoanOutstandingAmount";
            if (loan.RecoveryMode == "Manual")
                continue;
            if (!currentScheduleByLoan.TryGetValue(loan.Id, out var schedule))
                return "MissingCurrentLoanSchedule";
            var dueLines = scheduleLines.Where(row => row.ScheduleVersionId == schedule.Id
                    && row.DueDate >= periodStart && row.DueDate <= periodEnd)
                .ToArray();
            if (dueLines.GroupBy(row => row.InstallmentNo).Any(group => group.Count() > 1))
                return "DuplicateLoanInstallment";
            foreach (var installment in dueLines)
            {
                if (installment.PlannedAmount < 0m)
                    return "InvalidLoanInstallmentAmount";
                var allocated = allocationAmounts.GetValueOrDefault(
                    (schedule.Id, installment.InstallmentNo));
                if (allocated > installment.PlannedAmount)
                    return "InvalidLoanRepaymentAllocation";
                var sourceId = $"{loan.Id}:{schedule.VersionNo}:{installment.InstallmentNo}";
                if (priorRunSources.Any(source =>
                        source.SourceType == "LoanRecovery" && source.SourceId == sourceId))
                    return "LoanInstallmentAlreadyAssigned";
            }
        }
        return null;
    }

    private static PolicyCalculationAttempt CalculatePolicyLines(
        PayrollCalculationPolicyVersion? policy,
        string? policyPeriodError,
        IReadOnlyList<CalculatedLine> salaryLines,
        PayrollPeriod period,
        IReadOnlyCollection<HrDailyAttendance> attendance,
        IReadOnlyCollection<HrRosterAssignment> rosters,
        IReadOnlyCollection<HrLeaveRequest> leaveRequests,
        IReadOnlyDictionary<long, HrLeaveType> leaveTypes,
        IReadOnlyCollection<HrOvertimeEntry> overtime,
        IReadOnlyCollection<AdjustmentResolution> adjustments)
    {
        var approvedLeaveDays = leaveRequests.Where(request => request.Status == "Approved")
            .SelectMany(request => request.Days.Select(day => new { Request = request, Day = day }))
            .Where(row => row.Day.WorkDate >= period.PeriodStart && row.Day.WorkDate <= period.PeriodEnd)
            .ToArray();
        var unpaidLeaveDays = new List<(HrLeaveRequest Request, HrLeaveRequestDay Day)>();
        foreach (var leave in approvedLeaveDays.Where(row => row.Day.DayClassification == "NormalWorkingDay"))
        {
            if (!leaveTypes.TryGetValue(leave.Request.LeaveTypeId, out var leaveType))
                return PolicyCalculationAttempt.Failed("MissingLeaveTypeEvidence");
            if (!leaveType.IsPaid)
                unpaidLeaveDays.Add((leave.Request, leave.Day));
        }

        var leaveDates = approvedLeaveDays.Select(row => row.Day.WorkDate).ToHashSet();
        var absenceRows = attendance.Where(row => row.WorkDate >= period.PeriodStart
                && row.WorkDate <= period.PeriodEnd && row.Status == "Absent"
                && row.DayClassification == "NormalWorkingDay"
                && !leaveDates.Contains(row.WorkDate))
            .ToArray();
        var partialAttendance = attendance.Any(row => row.WorkDate >= period.PeriodStart
            && row.WorkDate <= period.PeriodEnd && row.Status == "HalfDay"
            && !leaveDates.Contains(row.WorkDate));
        var lateRows = attendance.Where(row => row.WorkDate >= period.PeriodStart
                && row.WorkDate <= period.PeriodEnd && row.LateMinutes > 0
                && row.Status != "Absent" && !leaveDates.Contains(row.WorkDate))
            .ToArray();
        var earlyRows = attendance.Where(row => row.WorkDate >= period.PeriodStart
                && row.WorkDate <= period.PeriodEnd && row.EarlyLeaveMinutes > 0
                && row.Status != "Absent" && !leaveDates.Contains(row.WorkDate))
            .ToArray();
        var statutoryAdjustments = adjustments.Where(row => row.IsApplicable
            && row.Row.Classification == "Statutory").ToArray();
        var hasPricedInputs = absenceRows.Length > 0 || unpaidLeaveDays.Count > 0
            || lateRows.Length > 0 || earlyRows.Length > 0 || overtime.Count > 0
            || statutoryAdjustments.Length > 0;
        if (policyPeriodError is not null && hasPricedInputs)
            return PolicyCalculationAttempt.Failed(policyPeriodError);
        if (policy is null)
        {
            if (absenceRows.Length > 0)
                return PolicyCalculationAttempt.Failed("AttendanceDeductionPolicyNotConfigured");
            if (unpaidLeaveDays.Count > 0)
                return PolicyCalculationAttempt.Failed("UnpaidLeaveDeductionPolicyNotConfigured");
            if (lateRows.Length > 0)
                return PolicyCalculationAttempt.Failed("AttendanceLatePricingNotConfigured");
            if (overtime.Count > 0)
                return PolicyCalculationAttempt.Failed("OvertimePayrollRateAndComponentNotConfigured");
            if (statutoryAdjustments.Length > 0)
                return PolicyCalculationAttempt.Failed("TaxStatutoryPolicyMappingNotConfigured");
            if (partialAttendance)
                return PolicyCalculationAttempt.Failed("AttendancePartialDayDeductionPolicyNotConfigured");
            if (attendance.Any(row => row.WorkDate >= period.PeriodStart
                && row.WorkDate <= period.PeriodEnd && row.Status == "Late" && row.LateMinutes <= 0))
                return PolicyCalculationAttempt.Failed("IncompleteAttendanceEvidence");
            return PolicyCalculationAttempt.Success([]);
        }

        var attendancePricingEnabled = policy.DeductAbsence
            || policy.LatePerMinuteRate.HasValue
            || policy.EarlyLeavePerMinuteRate.HasValue
            || policy.OvertimeMode is not null;
        if (attendancePricingEnabled && rosters.Any(roster =>
                roster.WorkDate >= period.PeriodStart && roster.WorkDate <= period.PeriodEnd
                && !attendance.Any(row => row.RosterAssignmentId == roster.Id)))
            return PolicyCalculationAttempt.Failed("IncompleteAttendanceEvidence");

        if (partialAttendance)
            return PolicyCalculationAttempt.Failed("AttendancePartialDayDeductionPolicyNotConfigured");
        foreach (var adjustment in statutoryAdjustments)
        {
            if (policy.TaxStatutoryComponentId is null
                || adjustment.Row.SalaryComponentId != policy.TaxStatutoryComponentId)
                return PolicyCalculationAttempt.Failed("TaxStatutoryPolicyMappingNotConfigured");
        }
        if (unpaidLeaveDays.Count > 0
            && (!policy.DeductUnpaidLeave || policy.UnpaidLeaveComponentId is null))
            return PolicyCalculationAttempt.Failed("UnpaidLeaveDeductionPolicyNotConfigured");
        if (absenceRows.Length > 0
            && policy.DeductAbsence && policy.AbsenceComponentId is null)
            return PolicyCalculationAttempt.Failed("AttendanceDeductionComponentNotConfigured");
        if (lateRows.Length > 0
            && (policy.LatePerMinuteRate is null || policy.LateComponentId is null))
            return PolicyCalculationAttempt.Failed("AttendanceLatePricingNotConfigured");
        if (overtime.Count > 0
            && (policy.OvertimeMode is null || policy.OvertimeComponentId is null
                || policy.StandardPaidMinutes <= 0))
            return PolicyCalculationAttempt.Failed("OvertimePayrollRateAndComponentNotConfigured");

        var needsSalaryBase = unpaidLeaveDays.Count > 0
            || absenceRows.Length > 0 && policy.DeductAbsence
            || overtime.Count > 0 && policy.OvertimeMode == "SalaryBaseMultiplier";
        decimal salaryBase = 0m;
        if (needsSalaryBase)
        {
            var salaryBaseIds = JsonSerializer.Deserialize<long[]>(
                    policy.SalaryBaseComponentIdsJson, JsonOptions)
                ?? throw new InvalidOperationException("Payroll calculation policy salary base is invalid.");
            if (salaryBaseIds.Length == 0)
                return PolicyCalculationAttempt.Failed("DeductionSalaryBaseNotConfigured");
            var salaryBaseLines = salaryLines.Where(line => salaryBaseIds.Contains(line.SalaryComponentId)
                    && line.LineType == "Earning")
                .ToArray();
            if (salaryBaseLines.Select(line => line.SalaryComponentId).Distinct().Count() != salaryBaseIds.Length)
                return PolicyCalculationAttempt.Failed("MissingDeductionSalaryBaseComponent");
            salaryBase = salaryBaseLines.Sum(line => line.Amount);
            if (salaryBase <= 0m)
                return PolicyCalculationAttempt.Failed("InvalidDeductionSalaryBase");
        }

        var scheduledWorkingDaysByMonth = rosters
            .Where(row => row.DayClassification == "NormalWorkingDay")
            .GroupBy(row => (row.WorkDate.Year, row.WorkDate.Month))
            .ToDictionary(group => group.Key, group => group.Select(row => row.WorkDate).Distinct().Count());

        int DivisorDays(DateOnly workDate) => policy.PartialMonthBasis switch
        {
            "Fixed30" => 30,
            "CalendarDays" => DateTime.DaysInMonth(workDate.Year, workDate.Month),
            "WorkingDays" => scheduledWorkingDaysByMonth.GetValueOrDefault(
                (workDate.Year, workDate.Month)),
            _ => 0
        };

        var outputMappings = ReadOutputMappings(policy.ConfigurationSnapshotJson);
        var lines = new List<PayrollEmployeeResultLine>();
        if (policy.DeductAbsence)
        {
            if (absenceRows.Length > 0 && policy.AbsenceComponentId is null)
                return PolicyCalculationAttempt.Failed("AttendanceDeductionComponentNotConfigured");
            var mapping = FindMapping(outputMappings, policy.AbsenceComponentId);
            foreach (var row in absenceRows)
            {
                var divisorDays = DivisorDays(row.WorkDate);
                if (divisorDays <= 0)
                    return PolicyCalculationAttempt.Failed("PayrollCalculationDivisorNotAvailable");
                var rate = salaryBase / divisorDays;
                lines.Add(CreatePricedLine(mapping, rate, "AttendanceAbsence",
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    row.ProcessingVersion, 1m, rate, "ProcessedAbsence"));
            }
        }
        foreach (var leave in unpaidLeaveDays)
        {
            var mapping = FindMapping(outputMappings, policy.UnpaidLeaveComponentId);
            var divisorDays = DivisorDays(leave.Day.WorkDate);
            if (divisorDays <= 0)
                return PolicyCalculationAttempt.Failed("PayrollCalculationDivisorNotAvailable");
            var rate = salaryBase / divisorDays;
            var amount = rate * leave.Day.Units;
            lines.Add(CreatePricedLine(mapping, amount, "UnpaidLeave",
                $"{leave.Request.Id}:{leave.Day.Id}", leave.Day.PolicyVersionId,
                leave.Day.Units, rate, "ApprovedUnpaidLeaveSnapshot"));
        }
        foreach (var row in lateRows)
        {
            var mapping = FindMapping(outputMappings, policy.LateComponentId);
            var amount = policy.LatePerMinuteRate!.Value * row.LateMinutes;
            lines.Add(CreatePricedLine(mapping, amount, "AttendanceLate",
                row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.ProcessingVersion, row.LateMinutes, policy.LatePerMinuteRate.Value,
                "ProcessedLateMinutes"));
        }
        if (policy.EarlyLeavePerMinuteRate.HasValue)
        {
            foreach (var row in earlyRows)
            {
                var mapping = FindMapping(outputMappings, policy.EarlyLeaveComponentId);
                var amount = policy.EarlyLeavePerMinuteRate.Value * row.EarlyLeaveMinutes;
                lines.Add(CreatePricedLine(mapping, amount, "AttendanceEarlyLeave",
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    row.ProcessingVersion, row.EarlyLeaveMinutes, policy.EarlyLeavePerMinuteRate.Value,
                    "ProcessedEarlyLeaveMinutes"));
            }
        }
        foreach (var row in overtime)
        {
            var rate = OvertimeRate(policy, row.DayCategory);
            if (rate is null)
                return PolicyCalculationAttempt.Failed("UnsupportedOvertimeDayCategory");
            var approvedMinutes = row.ApprovedMinutes!.Value;
            var hourlyRate = policy.OvertimeMode switch
            {
                "FixedHourlyRate" => rate.Value,
                "SalaryBaseMultiplier" =>
                    salaryBase * rate.Value * 60m
                        / policy.OvertimeDivisorDays / policy.StandardPaidMinutes,
                _ => throw new InvalidOperationException("Payroll overtime mode is invalid.")
            };
            var amount = hourlyRate * approvedMinutes / 60m;
            var mapping = FindMapping(outputMappings, policy.OvertimeComponentId);
            lines.Add(CreatePricedLine(mapping, amount, "ApprovedOvertime",
                row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.CalculationVersion, approvedMinutes, hourlyRate,
                $"Approved{row.DayCategory}Overtime"));
        }

        return PolicyCalculationAttempt.Success(lines);
    }

    private static PayrollCalculationPolicyComponentSnapshot[] ReadOutputMappings(string snapshotJson)
    {
        using var document = JsonDocument.Parse(snapshotJson);
        if (!document.RootElement.TryGetProperty("outputComponents", out var mappings))
            throw new InvalidOperationException("Payroll calculation policy output mappings are missing.");
        return JsonSerializer.Deserialize<PayrollCalculationPolicyComponentSnapshot[]>(
                mappings.GetRawText(), JsonOptions)
            ?? throw new InvalidOperationException("Payroll calculation policy output mappings are invalid.");
    }

    private static PayrollCalculationPolicyComponentSnapshot FindMapping(
        IReadOnlyCollection<PayrollCalculationPolicyComponentSnapshot> mappings,
        long? componentId)
    {
        if (componentId is null)
            throw new InvalidOperationException("A required Payroll output component is not configured.");
        return mappings.SingleOrDefault(row => row.Id == componentId.Value)
            ?? throw new InvalidOperationException("A Payroll output component snapshot is missing.");
    }

    private static PayrollEmployeeResultLine CreatePricedLine(
        PayrollCalculationPolicyComponentSnapshot mapping,
        decimal amount,
        string sourceType,
        string sourceId,
        long sourceVersion,
        decimal quantity,
        decimal rate,
        string evidenceCode) =>
        new()
        {
            SalaryComponentId = mapping.Id,
            ComponentCodeSnapshot = mapping.Code,
            ComponentNameSnapshot = mapping.Name,
            LineType = mapping.ComponentType,
            Amount = amount,
            SourceType = sourceType,
            SourceId = sourceId,
            SourceVersion = sourceVersion,
            InputQuantity = quantity,
            InputRate = rate,
            InputAmount = amount,
            EvidenceCode = evidenceCode
        };

    private static decimal? OvertimeRate(PayrollCalculationPolicyVersion policy, string dayCategory) =>
        dayCategory switch
        {
            "NormalDay" or "NormalWorkingDay" => policy.NormalDayOvertimeRate,
            "WeeklyOff" or "WeeklyOffDay" => policy.WeeklyOffOvertimeRate,
            "Holiday" => policy.HolidayOvertimeRate,
            "SpecialDay" => policy.SpecialDayOvertimeRate,
            _ => null
        };

    private static decimal RoundMoney(decimal amount, int precision, MidpointRounding roundingMode) =>
        decimal.Round(amount, precision, roundingMode);

    public async Task<PayrollEmployeeResultPageResponse> GetPageAsync(
        long businessId, long periodId, long runId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        var run = await GetRunAsync(businessId, periodId, runId, cancellationToken);
        var query = db.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == runId
                && row.CalculationVersion == run.CalculationVersion);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(row => row.EmployeeCodeSnapshot).ThenBy(row => row.EmployeeId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        EnsureSnapshotIntegrity(rows);
        return new PayrollEmployeeResultPageResponse(
            runId, page, pageSize, total, rows.Select(Map).ToArray());
    }

    public async Task<PayrollEmployeeResultDetailsResponse> GetAsync(
        long businessId, long periodId, long runId, long employeeId,
        CancellationToken cancellationToken)
    {
        var run = await GetRunAsync(businessId, periodId, runId, cancellationToken);
        var result = await db.PayrollEmployeeResults.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.PayrollRunId == runId
                && row.CalculationVersion == run.CalculationVersion && row.EmployeeId == employeeId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Payroll employee result not found.");
        EnsureSnapshotIntegrity([result]);
        var lines = await db.PayrollEmployeeResultLines.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollEmployeeResultId == result.Id)
            .OrderBy(row => row.CalculationOrder).ThenBy(row => row.ComponentCodeSnapshot)
            .Select(row => new PayrollEmployeeResultLineResponse(
                row.SalaryComponentId, row.ComponentCodeSnapshot, row.ComponentNameSnapshot,
                row.LineType, row.Amount, row.CalculationOrder, row.SourceType, row.SourceId,
                row.SourceVersion, row.InputQuantity, row.InputRate, row.InputAmount, row.EvidenceCode))
            .ToArrayAsync(cancellationToken);
        return new PayrollEmployeeResultDetailsResponse(Map(result), lines);
    }

    private async Task<PayrollRun> GetRunAsync(
        long businessId, long periodId, long runId, CancellationToken cancellationToken)
    {
        return await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.PayrollPeriodId == periodId && row.Id == runId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Payroll run not found.");
    }

    private static CalculationAttempt CalculateSalaryLines(
        string componentSnapshotJson, PayrollPeriod period, DateOnly salaryEffectiveFrom)
    {
        PayrollSalarySetupComponentSnapshot[] components;
        try
        {
            components = JsonSerializer.Deserialize<PayrollSalarySetupComponentSnapshot[]>(
                    componentSnapshotJson, JsonOptions)
                ?? throw new JsonException("Salary component snapshot is empty.");
        }
        catch (JsonException)
        {
            return CalculationAttempt.Failed("InvalidSalarySnapshot");
        }

        var ordered = components.OrderBy(component => component.CalculationOrder)
            .ThenBy(component => component.SalaryComponentId).ToArray();
        if (ordered.Select(component => component.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != ordered.Length)
            return CalculationAttempt.Failed("InvalidSalarySnapshot");

        var byCode = ordered.ToDictionary(component => component.Code, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var component in ordered)
                Evaluate(component);
        }
        catch (CalculationException exception)
        {
            return CalculationAttempt.Failed(exception.Code);
        }
        catch (ArgumentException)
        {
            return CalculationAttempt.Failed("InvalidSalaryFormula");
        }
        catch (OverflowException)
        {
            return CalculationAttempt.Failed("CalculationOverflow");
        }
        catch (DivideByZeroException)
        {
            return CalculationAttempt.Failed("InvalidSalaryFormula");
        }

        var lines = new List<CalculatedLine>(ordered.Length);
        foreach (var component in ordered)
        {
            if (!IsDue(component, period, salaryEffectiveFrom))
                continue;
            var amount = values[component.Code];
            if (amount < 0)
                return CalculationAttempt.Failed("NegativeComponentAmount");
            if (component.ComponentType is not ("Earning" or "Deduction"))
                return CalculationAttempt.Failed("InvalidComponentType");
            lines.Add(new CalculatedLine(
                component.SalaryComponentId, component.Code, component.Name,
                component.ComponentType, amount, component.CalculationOrder));
        }
        return CalculationAttempt.Success(lines);

        decimal Evaluate(PayrollSalarySetupComponentSnapshot component)
        {
            if (values.TryGetValue(component.Code, out var existing))
                return existing;
            if (!visiting.Add(component.Code))
                throw new CalculationException("InvalidSalaryFormula");

            decimal value;
            if (!IsDue(component, period, salaryEffectiveFrom))
                value = 0m;
            else if (component.ValueType == "Fixed" && component.Value.HasValue)
                value = component.Value.Value;
            else if (component.ValueType == "Formula" && !string.IsNullOrWhiteSpace(component.FormulaExpression))
            {
                var references = PayrollSalaryFormulaRules.ParseReferences(component.FormulaExpression);
                foreach (var reference in references)
                {
                    if (!byCode.TryGetValue(reference, out var dependency))
                        throw new CalculationException("InvalidSalaryFormula");
                    Evaluate(dependency);
                }
                value = PayrollSalaryFormulaEvaluator.Evaluate(component.FormulaExpression, values);
            }
            else if (component.ValueType == "Percentage")
                throw new CalculationException("PercentageBasisNotConfigured");
            else if (component.ValueType == "Calculated")
                throw new CalculationException("ExternalCalculationInputUnavailable");
            else
                throw new CalculationException("InvalidSalaryComponentValue");

            visiting.Remove(component.Code);
            values.Add(component.Code, value);
            return value;
        }
    }

    private static bool IsDue(
        PayrollSalarySetupComponentSnapshot component,
        PayrollPeriod period,
        DateOnly salaryEffectiveFrom) =>
        component.IsRecurring
        || salaryEffectiveFrom >= period.PeriodStart && salaryEffectiveFrom <= period.PeriodEnd;

    private static PayrollRunCalculationResponse Summarize(
        long runId, IReadOnlyCollection<PayrollEmployeeResult> results, bool isReplay) =>
        new(runId,
            results.Count(result => result.Status == PayrollEmployeeResultStatuses.Calculated),
            results.Count(result => result.Status == PayrollEmployeeResultStatuses.Exception),
            isReplay);

    private static PayrollEmployeeResultSummary Map(PayrollEmployeeResult result) =>
        new(result.EmployeeId, result.EmployeeCodeSnapshot, result.EmployeeNameSnapshot,
            result.LocationIdSnapshot, result.SalaryVersionId, result.SalaryEffectiveFromSnapshot,
            result.Status, result.ExceptionCode, result.GrossEarnings, result.TotalDeductions,
            result.NetPay, result.CalculationSnapshotHash);

    private static void EnsureSnapshotIntegrity(IEnumerable<PayrollEmployeeResult> results)
    {
        foreach (var result in results)
        {
            if (result.CalculationSnapshotJson is null && result.CalculationSnapshotHash is null)
                continue;
            if (result.CalculationSnapshotJson is null || result.CalculationSnapshotHash is null
                || !StringComparer.Ordinal.Equals(
                    HashCanonicalJson(result.CalculationSnapshotJson), result.CalculationSnapshotHash))
                throw new InvalidOperationException("Payroll calculation snapshot integrity validation failed.");
        }
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 200 || page > int.MaxValue / pageSize)
            throw new ArgumentException("Payroll result page is outside the supported range.");
    }

    private async Task LockBusinessAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payroll calculations require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 8)})", cancellationToken);
    }

    private void EnsureAuditedTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payroll calculations require the audited transaction boundary.");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollRunConflictException("Payroll run changed concurrently.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PayrollRunConflictException("Payroll results conflict with another calculation.", exception);
        }
    }

    private sealed record CalculatedLine(
        long SalaryComponentId, string Code, string Name, string LineType,
        decimal Amount, int CalculationOrder);

    private sealed record AdjustmentResolution(
        PayrollEmployeeAdjustmentVersion Row, bool IsApplicable, string? ExceptionCode);

    private sealed record PriorSource(string SourceType, string? SourceId);

    private sealed record CalculationAttempt(
        IReadOnlyList<CalculatedLine> Lines, string? ExceptionCode)
    {
        public static CalculationAttempt Success(IReadOnlyList<CalculatedLine> lines) => new(lines, null);
        public static CalculationAttempt Failed(string code) => new([], code);
    }

    private sealed record PolicyCalculationAttempt(
        IReadOnlyList<PayrollEmployeeResultLine> Lines, string? ExceptionCode)
    {
        public static PolicyCalculationAttempt Success(
            IReadOnlyList<PayrollEmployeeResultLine> lines) => new(lines, null);
        public static PolicyCalculationAttempt Failed(string code) => new([], code);
    }

    private sealed record PayrollCalculationPolicyComponentSnapshot(
        long Id, string Code, string Name, string ComponentType);

    private sealed record PayrollEmployeePresentationSnapshot(
        long? PositionId,
        string? PositionCode,
        string? PositionName,
        string? LocationName,
        string? LocationReference,
        string? LocationType);

    private sealed class CalculationException(string code) : Exception
    {
        public string Code { get; } = code;
    }
}
