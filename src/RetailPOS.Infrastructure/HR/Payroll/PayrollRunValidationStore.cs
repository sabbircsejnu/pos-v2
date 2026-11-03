using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollRunValidationStore(RetailPOSDbContext db) : IPayrollRunValidationStore
{
    public async Task<PayrollRunValidationResponse> ValidateAsync(
        long businessId,
        long periodId,
        PayrollRunResponse run,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (run.BusinessId != businessId || run.PayrollPeriodId != periodId)
            throw new KeyNotFoundException("Payroll run was not found.");

        var persistedRun = await db.PayrollRuns.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.PayrollPeriodId == periodId && row.Id == run.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll run was not found.");
        var selections = await db.PayrollRunEmployeeSelections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == run.Id)
            .OrderBy(row => row.EmployeeId)
            .ToArrayAsync(cancellationToken);
        var includedEmployeeIds = selections.Where(row => row.IsIncluded)
            .Select(row => row.EmployeeId).Distinct().ToList();
        var results = persistedRun.CalculationVersion <= 0
            ? []
            : await db.PayrollEmployeeResults.AsNoTracking()
                .Where(row => row.BusinessId == businessId && row.PayrollRunId == run.Id
                    && row.CalculationVersion == persistedRun.CalculationVersion)
                .OrderBy(row => row.EmployeeId)
                .ToArrayAsync(cancellationToken);

        var findings = new List<PayrollRunValidationFinding>();
        var isCalculated = persistedRun.CalculationVersion > 0;
        if (!isCalculated)
        {
            findings.Add(Blocking("CalculationRequired", "Calculation",
                "Calculate the Payroll run before reviewing its input validation."));
        }

        if (run.MonetaryConfigurationVersionId is null || run.CurrencyCode is null
            || run.DecimalPrecision is null || run.RoundingMode is null)
        {
            findings.Add(Blocking("RequiredMonetaryConfigurationMissing", "Configuration",
                "A Business-scoped monetary configuration is required for this Payroll run."));
        }

        if (includedEmployeeIds.Count == 0)
        {
            findings.Add(Blocking("NoIncludedEmployees", "EmployeeEligibility",
                "The Payroll run has no eligible employees."));
        }

        foreach (var selection in selections.Where(row => !row.IsIncluded))
        {
            switch (selection.ExclusionReasonCode)
            {
                case "NoApprovedSalary":
                case "SalaryNotYetEffective":
                    findings.Add(Blocking(
                        selection.ExclusionReasonCode,
                        "Salary",
                        selection.ExclusionReasonCode == "NoApprovedSalary"
                            ? "No approved salary is effective for this employee on the eligibility date."
                            : "The employee's approved salary is not effective on the eligibility date.",
                        selection.EmployeeId));
                    break;
                case "AmbiguousEmploymentPeriod":
                case "MissingPrimaryLocation":
                    findings.Add(Blocking(
                        selection.ExclusionReasonCode,
                        "EmployeeEligibility",
                        "Employee eligibility evidence is incomplete or ambiguous.",
                        selection.EmployeeId));
                    break;
                case "NoEmploymentOverlap":
                case "InactiveOrSuspended":
                    findings.Add(Warning(
                        selection.ExclusionReasonCode,
                        "EmployeeEligibility",
                        "The employee is excluded from this Payroll run by the captured eligibility rules.",
                        selection.EmployeeId));
                    break;
            }
        }

        if (includedEmployeeIds.Count > 0)
        {
            var pendingLeaveEmployeeIds = await db.HrLeaveRequests.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && includedEmployeeIds.Contains(row.EmployeeId)
                    && row.Status == "PendingApproval"
                    && row.StartDate <= run.PeriodEnd
                    && row.EndDate >= run.PeriodStart)
                .Select(row => row.EmployeeId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            foreach (var employeeId in pendingLeaveEmployeeIds)
            {
                findings.Add(Warning("LeavePendingApproval", "Leave",
                    "A leave request pending approval overlaps the Payroll period.", employeeId));
            }

            var pendingOvertimeEmployeeIds = await db.HrOvertimeEntries.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && includedEmployeeIds.Contains(row.EmployeeId)
                    && (row.Status == "Calculated" || row.Status == "PendingApproval")
                    && row.WorkDate >= run.PeriodStart && row.WorkDate <= run.PeriodEnd)
                .Select(row => row.EmployeeId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            foreach (var employeeId in pendingOvertimeEmployeeIds)
            {
                findings.Add(Warning("OvertimePendingApproval", "Overtime",
                    "An overtime entry is not yet approved and Payroll-eligible.", employeeId));
            }

            var otherRunIds = db.PayrollRuns.AsNoTracking()
                .Where(row => row.BusinessId == businessId && row.PayrollPeriodId == periodId
                    && row.Id != run.Id
                    && (!run.OriginalPayrollRunId.HasValue
                        || row.Id != run.OriginalPayrollRunId.Value)
                    && row.Status != PayrollRunStatuses.Cancelled)
                .Select(row => row.Id);
            if (await otherRunIds.AnyAsync(cancellationToken))
            {
                var duplicates = await db.PayrollRunEmployeeSelections.AsNoTracking()
                    .Where(row => row.BusinessId == businessId && otherRunIds.Contains(row.PayrollRunId)
                        && row.IsIncluded && includedEmployeeIds.Contains(row.EmployeeId))
                    .Select(row => new { row.EmployeeId, row.PayrollRunId })
                    .Distinct()
                    .OrderBy(row => row.EmployeeId).ThenBy(row => row.PayrollRunId)
                    .ToArrayAsync(cancellationToken);
                foreach (var duplicate in duplicates)
                {
                    findings.Add(Blocking("DuplicateEmployeePayroll", "DuplicatePayroll",
                        "The employee is already included in another non-cancelled run for this period.",
                        duplicate.EmployeeId, duplicate.PayrollRunId));
                }
            }
        }

        if (isCalculated)
        {
            var resultByEmployee = results.ToDictionary(row => row.EmployeeId);
            foreach (var employeeId in includedEmployeeIds)
            {
                if (!resultByEmployee.TryGetValue(employeeId, out var result))
                {
                    findings.Add(Blocking("MissingCalculationResult", "Calculation",
                        "The included employee has no result for the current calculation version.",
                        employeeId));
                    continue;
                }

                if (result.Status == PayrollEmployeeResultStatuses.Exception)
                {
                    var code = result.ExceptionCode ?? "UnclassifiedCalculationException";
                    findings.Add(Blocking(code, CategorizeCalculationException(code),
                        CalculationMessage(code), employeeId));
                }

                if (result.NetPay is < 0m)
                {
                    findings.Add(Blocking("NegativeNetPay", "NetPay",
                        "Calculated net pay is negative and must be corrected before submission.",
                        employeeId));
                }
            }
        }

        if (includedEmployeeIds.Count > 0)
        {
            var destinations = await db.PayrollEmployeePaymentDestinations.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && includedEmployeeIds.Contains(row.EmployeeId)
                    && row.EffectiveFrom <= run.PeriodEnd)
                .OrderBy(row => row.EmployeeId)
                .ThenByDescending(row => row.EffectiveFrom)
                .ThenByDescending(row => row.VersionNo)
                .ToArrayAsync(cancellationToken);

            foreach (var employeeId in includedEmployeeIds)
            {
                var employeeDestinations = destinations
                    .Where(row => row.EmployeeId == employeeId).ToArray();
                if (employeeDestinations.Length == 0)
                {
                    findings.Add(Blocking("MissingPaymentDestination", "PaymentDetails",
                        "No active payment destination is effective for this employee by the Payroll period end date.",
                        employeeId));
                    continue;
                }

                var effectiveFrom = employeeDestinations[0].EffectiveFrom;
                var effectiveRows = employeeDestinations
                    .Where(row => row.EffectiveFrom == effectiveFrom).ToArray();
                if (effectiveRows.Length != 1)
                {
                    findings.Add(Blocking("AmbiguousPaymentDestination", "PaymentDetails",
                        "More than one payment destination version is effective for this employee on the Payroll period end date.",
                        employeeId));
                    continue;
                }

                var destination = effectiveRows[0];
                if (!destination.IsActive || !destination.IsPrimary)
                {
                    findings.Add(Blocking("MissingPaymentDestination", "PaymentDetails",
                        "No active primary payment destination is effective for this employee by the Payroll period end date.",
                        employeeId));
                    continue;
                }

                switch (destination.Method)
                {
                    case PayrollPaymentDestinationMethods.Cash:
                        break;
                    case PayrollPaymentDestinationMethods.Bank
                        when string.IsNullOrWhiteSpace(destination.BankName)
                            || string.IsNullOrWhiteSpace(destination.AccountName)
                            || string.IsNullOrWhiteSpace(destination.AccountNumber):
                        findings.Add(Blocking("InvalidBankPaymentDestination", "PaymentDetails",
                            "The effective bank payment destination is incomplete.", employeeId));
                        break;
                    case PayrollPaymentDestinationMethods.Mobile
                        when string.IsNullOrWhiteSpace(destination.MobileProvider)
                            || string.IsNullOrWhiteSpace(destination.MobileAccountName)
                            || string.IsNullOrWhiteSpace(destination.MobileAccountNumber):
                        findings.Add(Blocking("InvalidMobilePaymentDestination", "PaymentDetails",
                            "The effective mobile payment destination is incomplete.", employeeId));
                        break;
                    case PayrollPaymentDestinationMethods.Bank:
                    case PayrollPaymentDestinationMethods.Mobile:
                        break;
                    default:
                        findings.Add(Blocking("MissingPaymentDestination", "PaymentDetails",
                            "The effective payment destination method is unsupported.", employeeId));
                        break;
                }
            }
        }

        var orderedFindings = findings
            .OrderBy(row => row.Severity == PayrollValidationSeverities.BlockingError ? 0 : 1)
            .ThenBy(row => row.EmployeeId)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .ThenBy(row => row.RelatedRunId)
            .ToArray();
        var blockingErrorCount = orderedFindings.Count(
            row => row.Severity == PayrollValidationSeverities.BlockingError);
        var warningCount = orderedFindings.Count(
            row => row.Severity == PayrollValidationSeverities.Warning);
        var offset = checked((page - 1) * pageSize);

        return new PayrollRunValidationResponse(
            run.Id,
            periodId,
            persistedRun.Status,
            persistedRun.CalculationVersion,
            isCalculated,
            blockingErrorCount == 0,
            blockingErrorCount,
            warningCount,
            page,
            pageSize,
            orderedFindings.Length,
            orderedFindings.Skip(offset).Take(pageSize).ToArray());
    }

    private static PayrollRunValidationFinding Blocking(
        string code,
        string category,
        string message,
        long? employeeId = null,
        long? relatedRunId = null) =>
        new(code, category, PayrollValidationSeverities.BlockingError, employeeId, relatedRunId, message);

    private static PayrollRunValidationFinding Warning(
        string code,
        string category,
        string message,
        long? employeeId = null) =>
        new(code, category, PayrollValidationSeverities.Warning, employeeId, null, message);

    private static string CategorizeCalculationException(string code) =>
        code.Contains("Attendance", StringComparison.Ordinal)
            || code.Contains("Roster", StringComparison.Ordinal)
            || code.Contains("Shift", StringComparison.Ordinal)
                ? "AttendanceShift"
                : code.Contains("Overtime", StringComparison.Ordinal)
                    ? "Overtime"
                    : code.Contains("Loan", StringComparison.Ordinal)
                        || code.Contains("Repayment", StringComparison.Ordinal)
                            ? "LoanRecovery"
                            : code.Contains("Policy", StringComparison.Ordinal)
                                || code.Contains("Configuration", StringComparison.Ordinal)
                                    ? "Configuration"
                                    : "PayrollCalculation";

    private static string CalculationMessage(string code) =>
        code switch
        {
            "NegativeNetPay" => "Calculated net pay is negative and must be corrected before submission.",
            "IncompleteAttendanceEvidence" or "AttendanceLeaveEvidenceNotApproved"
                or "AttendancePartialDayDeductionPolicyNotConfigured"
                or "AttendanceDeductionPolicyNotConfigured"
                or "AttendanceLatePricingNotConfigured"
                or "AttendanceDeductionComponentNotConfigured"
                or "ConflictingApprovedLeaveDays" =>
                "Attendance, shift, or leave evidence is incomplete or inconsistent.",
            "IncompleteOvertimeApprovalEvidence" =>
                "Overtime approval evidence is incomplete or inconsistent.",
            "PayrollCalculationPolicyNotEffectiveForWholePeriod" =>
                "No single effective Payroll calculation policy covers the entire period.",
            "NoApprovedSalary" or "SalaryNotYetEffective" =>
                "No approved salary is effective for this employee on the eligibility date.",
            "InvalidLoanOutstandingAmount" or "MissingCurrentLoanSchedule"
                or "DuplicateLoanInstallment" or "InvalidLoanInstallmentAmount"
                or "InvalidLoanRepaymentAllocation" or "LoanInstallmentAlreadyAssigned" =>
                "Loan recovery evidence is invalid, incomplete, or already consumed.",
            _ => "Payroll calculation input requires correction before submission."
        };
}
