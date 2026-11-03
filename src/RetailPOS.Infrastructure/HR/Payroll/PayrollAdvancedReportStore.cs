using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollAdvancedReportStore(RetailPOSDbContext db) : IPayrollAdvancedReportStore
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<PayrollAdvancedReportPage> QueryAsync(
        long businessId,
        PayrollAdvancedReportQuery request,
        HrOperationalLocationReadScope locationScope,
        CancellationToken cancellationToken)
    {
        if (locationScope.BusinessId != businessId)
            throw new UnauthorizedAccessException("Payroll report scope is outside Business.");

        var allowedLocationIds = await ResolveLocationIdsAsync(
            businessId, locationScope, cancellationToken);
        if (request.LocationId is long locationId
            && allowedLocationIds is not null && !allowedLocationIds.Contains(locationId))
            throw new UnauthorizedAccessException("The Payroll report location is outside the caller's scope.");

        if (request.Kind == "SalaryHistory")
            return await SalaryHistoryAsync(businessId, request, allowedLocationIds, cancellationToken);
        if (request.Kind == "LoanRecovery")
            return await LoanRecoveryAsync(businessId, request, allowedLocationIds, cancellationToken);
        if (request.Kind == "PayrollVariance")
            return await VarianceAsync(businessId, request, allowedLocationIds, cancellationToken);
        if (request.Kind == "PaymentOutstanding")
            return await PaymentOutstandingAsync(businessId, request, allowedLocationIds, cancellationToken);

        var runs =
            from run in db.PayrollRuns.AsNoTracking()
            join period in db.PayrollPeriods.AsNoTracking()
                on new { run.BusinessId, Id = run.PayrollPeriodId }
                equals new { period.BusinessId, period.Id }
            where run.BusinessId == businessId
                && period.PeriodStart <= request.To && period.PeriodEnd >= request.From
                && (request.RunId == null || run.Id == request.RunId)
            select new { Run = run, Period = period };

        if (request.Kind == "RunStatus")
        {
            var runStatus = from item in runs
                            where (request.LocationId == null || item.Run.ScopeId == request.LocationId)
                                && (allowedLocationIds == null
                                    || item.Run.ScopeType == PayrollRunScopeTypes.Location
                                        && item.Run.ScopeId.HasValue
                                        && allowedLocationIds.Contains(item.Run.ScopeId.Value)
                                    || db.PayrollEmployeeResults.AsNoTracking().Any(result =>
                                        result.BusinessId == businessId
                                        && result.PayrollRunId == item.Run.Id
                                        && result.LocationIdSnapshot.HasValue
                                        && allowedLocationIds.Contains(result.LocationIdSnapshot.Value)))
                            orderby item.Period.PeriodStart, item.Run.Id
                            select new PayrollAdvancedReportRow(
                                null, null, null, item.Period.Id, item.Period.PeriodCode,
                                item.Run.Id, item.Run.CalculationVersion, item.Run.RunType,
                                item.Run.OriginalPayrollRunId, "RunStatus", null, null,
                                null, null, null, item.Run.Status, item.Run.ScopeId,
                                null, null, null, null, null, null, null,
                                item.Run.CorrectionReason ?? item.Run.DecisionReason);
            return await PageAsync(request, runStatus, cancellationToken);
        }

        if (request.Kind == "CorrectionOffCycle")
        {
            var corrections = from item in runs
                              where item.Run.RunType != PayrollRunTypes.Regular
                                  && (request.LocationId == null || item.Run.ScopeId == request.LocationId
                                      || db.PayrollEmployeeResults.AsNoTracking().Any(result =>
                                          result.BusinessId == businessId
                                          && result.PayrollRunId == item.Run.Id
                                          && result.LocationIdSnapshot == request.LocationId))
                                  && (allowedLocationIds == null
                                      || item.Run.ScopeType == PayrollRunScopeTypes.Location
                                          && item.Run.ScopeId.HasValue
                                          && allowedLocationIds.Contains(item.Run.ScopeId.Value)
                                      || db.PayrollEmployeeResults.AsNoTracking().Any(result =>
                                          result.BusinessId == businessId
                                          && result.PayrollRunId == item.Run.Id
                                          && result.LocationIdSnapshot.HasValue
                                          && allowedLocationIds.Contains(result.LocationIdSnapshot.Value)))
                                      orderby item.Period.PeriodStart, item.Run.Id
                                      select new PayrollAdvancedReportRow(
                                  null, null, null, item.Period.Id, item.Period.PeriodCode,
                                  item.Run.Id, item.Run.CalculationVersion, item.Run.RunType,
                                  item.Run.OriginalPayrollRunId, "RunLineage", null, null,
                                  db.PayrollEmployeeResults.AsNoTracking()
                                      .Where(result => result.BusinessId == businessId
                                          && result.PayrollRunId == item.Run.Id
                                          && result.CalculationVersion == item.Run.CalculationVersion
                                          && result.Status == PayrollEmployeeResultStatuses.Calculated
                                          && (request.LocationId == null
                                              || result.LocationIdSnapshot == request.LocationId)
                                          && (allowedLocationIds == null
                                              || result.LocationIdSnapshot.HasValue
                                              && allowedLocationIds.Contains(result.LocationIdSnapshot.Value)))
                                      .Sum(result => (decimal?)result.GrossEarnings) ?? 0m,
                                  db.PayrollEmployeeResults.AsNoTracking()
                                      .Where(result => result.BusinessId == businessId
                                          && result.PayrollRunId == item.Run.Id
                                          && result.CalculationVersion == item.Run.CalculationVersion
                                          && result.Status == PayrollEmployeeResultStatuses.Calculated
                                          && (request.LocationId == null
                                              || result.LocationIdSnapshot == request.LocationId)
                                          && (allowedLocationIds == null
                                              || result.LocationIdSnapshot.HasValue
                                              && allowedLocationIds.Contains(result.LocationIdSnapshot.Value)))
                                      .Sum(result => (decimal?)result.TotalDeductions) ?? 0m,
                                  db.PayrollEmployeeResults.AsNoTracking()
                                      .Where(result => result.BusinessId == businessId
                                          && result.PayrollRunId == item.Run.Id
                                          && result.CalculationVersion == item.Run.CalculationVersion
                                          && result.Status == PayrollEmployeeResultStatuses.Calculated
                                          && (request.LocationId == null
                                              || result.LocationIdSnapshot == request.LocationId)
                                          && (allowedLocationIds == null
                                              || result.LocationIdSnapshot.HasValue
                                              && allowedLocationIds.Contains(result.LocationIdSnapshot.Value)))
                                      .Sum(result => (decimal?)result.NetPay) ?? 0m,
                                  item.Run.Status, item.Run.ScopeId,
                                  null, null, null, null, null, null, null,
                                  item.Run.CorrectionReference ?? item.Run.RunReference);
            return await PageAsync(request, corrections, cancellationToken);
        }

        var results = from item in runs
                      join result in db.PayrollEmployeeResults.AsNoTracking()
                          on new { item.Run.BusinessId, PayrollRunId = item.Run.Id }
                          equals new { result.BusinessId, result.PayrollRunId }
                      where result.CalculationVersion == item.Run.CalculationVersion
                          && result.Status == PayrollEmployeeResultStatuses.Calculated
                          && (request.EmployeeId == null || result.EmployeeId == request.EmployeeId)
                          && (request.LocationId == null || result.LocationIdSnapshot == request.LocationId)
                          && (allowedLocationIds == null || result.LocationIdSnapshot.HasValue
                              && allowedLocationIds.Contains(result.LocationIdSnapshot.Value))
                      select new { item.Run, item.Period, Result = result };

        var query = request.Kind switch
        {
            "PayrollRegister" =>
                from item in results
                join location in db.OperationalLocationProjections.AsNoTracking()
                    on new { item.Result.BusinessId, Id = item.Result.LocationIdSnapshot }
                    equals new { location.BusinessId, Id = (long?)location.Id } into locations
                from location in locations.DefaultIfEmpty()
                orderby item.Period.PeriodStart, item.Result.EmployeeCodeSnapshot,
                    item.Run.Id, item.Result.EmployeeId
                select new PayrollAdvancedReportRow(
                    item.Result.EmployeeId, item.Result.EmployeeCodeSnapshot,
                    item.Result.EmployeeNameSnapshot, item.Period.Id, item.Period.PeriodCode,
                    item.Run.Id, item.Run.CalculationVersion, item.Run.RunType,
                    item.Run.OriginalPayrollRunId, "PayrollRegister", null, null,
                    item.Result.GrossEarnings, item.Result.TotalDeductions, item.Result.NetPay,
                    item.Run.Status, item.Result.LocationIdSnapshot, location == null ? null : location.Name,
                    item.Period.PeriodStart, item.Period.PeriodEnd, null, null, null, null, null),
            "PayrollComponents" =>
                from item in results
                join line in db.PayrollEmployeeResultLines.AsNoTracking()
                    on new { item.Result.BusinessId, PayrollEmployeeResultId = item.Result.Id }
                    equals new { line.BusinessId, line.PayrollEmployeeResultId }
                orderby item.Period.PeriodStart, item.Result.EmployeeCodeSnapshot,
                    item.Run.Id, line.CalculationOrder, line.Id
                select new PayrollAdvancedReportRow(
                    item.Result.EmployeeId, item.Result.EmployeeCodeSnapshot,
                    item.Result.EmployeeNameSnapshot, item.Period.Id, item.Period.PeriodCode,
                    item.Run.Id, item.Run.CalculationVersion, item.Run.RunType,
                    item.Run.OriginalPayrollRunId, line.LineType, line.ComponentCodeSnapshot,
                    line.ComponentNameSnapshot, line.LineType == "Earning" ? line.Amount : null,
                    line.LineType == "Deduction" ? line.Amount : null, null, item.Run.Status,
                    item.Result.LocationIdSnapshot, null, item.Period.PeriodStart,
                    item.Period.PeriodEnd, null, null, null, null,
                    line.SourceType + (line.SourceId == null ? "" : ":" + line.SourceId)),
            "CostByLocation" =>
                from item in results
                join location in db.OperationalLocationProjections.AsNoTracking()
                    on new { item.Result.BusinessId, Id = item.Result.LocationIdSnapshot }
                    equals new { location.BusinessId, Id = (long?)location.Id } into locations
                from location in locations.DefaultIfEmpty()
                group new { item, location } by new
                {
                    item.Result.LocationIdSnapshot,
                    LocationName = location == null ? null : location.Name,
                    LocationType = location == null ? null : location.SourceEntityType
                } into grouped
                orderby grouped.Key.LocationName, grouped.Key.LocationIdSnapshot
                select new PayrollAdvancedReportRow(
                    null, null, null, null, null, null, null, null, null,
                    "LocationCost", null, null,
                    grouped.Sum(row => row.item.Result.GrossEarnings) ?? 0m,
                    grouped.Sum(row => row.item.Result.TotalDeductions) ?? 0m,
                    grouped.Sum(row => row.item.Result.NetPay) ?? 0m,
                    "Calculated",
                    grouped.Key.LocationIdSnapshot,
                    grouped.Key.LocationName,
                    request.From,
                    request.To,
                    null,
                    null,
                    null,
                    grouped.Sum(row => row.item.Result.NetPay) ?? 0m,
                    grouped.Key.LocationType == null
                        ? $"Employees: {grouped.Select(row => row.item.Result.EmployeeId).Distinct().LongCount()}; location type unavailable in historical projection."
                        : $"Employees: {grouped.Select(row => row.item.Result.EmployeeId).Distinct().LongCount()}; location type: {grouped.Key.LocationType}."),
            _ => throw new ArgumentException("Unknown Payroll report kind.", nameof(request))
        };

        return await PageAsync(request, query, cancellationToken);
    }

    private async Task<PayrollAdvancedReportPage> SalaryHistoryAsync(
        long businessId,
        PayrollAdvancedReportQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var versions = db.PayrollEmployeeSalaryVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.EffectiveFrom >= request.From && row.EffectiveFrom <= request.To
                && (request.EmployeeId == null || row.EmployeeId == request.EmployeeId)
                && (request.LocationId == null || row.OperationalLocationProjectionId == request.LocationId)
                && (allowedLocationIds == null
                    || allowedLocationIds.Contains(row.OperationalLocationProjectionId)))
            .OrderBy(row => row.EmployeeId).ThenBy(row => row.EffectiveFrom).ThenBy(row => row.Id);
        var totalCount = await versions.LongCountAsync(cancellationToken);
        var snapshotRows = await versions.Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(row => new
            {
                row.Id,
                row.EmployeeId,
                row.EffectiveFrom,
                row.OperationalLocationProjectionId,
                row.ComponentSnapshotJson,
                row.ApprovedByUserId,
                row.ApprovedAtUtc
            }).ToListAsync(cancellationToken);

        var employeeIds = snapshotRows.Select(row => row.EmployeeId).ToHashSet();
        var employees = await db.HrEmployees.AsNoTracking()
            .Where(row => row.BusinessId == businessId && employeeIds.Contains(row.Id))
            .Select(row => new { row.Id, row.EmployeeCode, row.DisplayName })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var locationIds = snapshotRows.Select(row => row.OperationalLocationProjectionId).ToHashSet();
        var locations = await db.OperationalLocationProjections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && locationIds.Contains(row.Id))
            .Select(row => new { row.Id, row.Name })
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        var rows = snapshotRows.Select(row =>
        {
            var components = JsonSerializer.Deserialize<PayrollSalarySetupComponentSnapshot[]>(
                row.ComponentSnapshotJson, SnapshotJsonOptions)
                ?? throw new InvalidDataException("Stored approved salary evidence is invalid.");
            var employee = employees.GetValueOrDefault(row.EmployeeId);
            var earnings = components.Where(component => component.ComponentType == "Earning")
                .Sum(component => component.Value ?? 0m);
            var deductions = components.Where(component => component.ComponentType == "Deduction")
                .Sum(component => component.Value ?? 0m);
            return new PayrollAdvancedReportRow(
                row.EmployeeId, employee?.EmployeeCode, employee?.DisplayName, null, null,
                null, null, "SalaryVersion", null, "ApprovedSalaryVersion", null, null,
                earnings, deductions, earnings - deductions, "Approved",
                row.OperationalLocationProjectionId,
                locations.GetValueOrDefault(row.OperationalLocationProjectionId)?.Name,
                row.EffectiveFrom, null, row.Id, null, null, null,
                $"Approved by {row.ApprovedByUserId} at {row.ApprovedAtUtc:O}");
        }).ToArray();
        return new PayrollAdvancedReportPage(
            request.Kind, rows, request.Page, request.PageSize, totalCount);
    }

    private async Task<PayrollAdvancedReportPage> LoanRecoveryAsync(
        long businessId,
        PayrollAdvancedReportQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var loans = db.PayrollEmployeeLoans.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && (request.EmployeeId == null || row.EmployeeId == request.EmployeeId)
                && (request.LocationId == null || row.OperationalLocationProjectionId == request.LocationId)
                && (allowedLocationIds == null
                    || allowedLocationIds.Contains(row.OperationalLocationProjectionId)));
        var totalCount = await loans.LongCountAsync(cancellationToken);
        var page = await loans.OrderBy(row => row.EmployeeId).ThenBy(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(row => new
            {
                row.Id, row.EmployeeId, row.OperationalLocationProjectionId, row.LoanType,
                row.IsOpeningBalance, row.OpeningBalanceAmount, row.ApprovedAmount, row.PrincipalAmount,
                row.OutstandingAmount, row.Status, row.ProposedScheduleJson
            }).ToListAsync(cancellationToken);
        var loanIds = page.Select(row => row.Id).ToHashSet();
        var repayments = await db.PayrollLoanRepayments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && loanIds.Contains(row.LoanId)
                && row.PaymentDate >= request.From && row.PaymentDate <= request.To)
            .GroupBy(row => row.LoanId)
            .Select(group => new { LoanId = group.Key, Amount = group.Sum(row => row.Amount) })
            .ToDictionaryAsync(row => row.LoanId, row => row.Amount, cancellationToken);

        var employeeIds = page.Select(item => item.EmployeeId).ToHashSet();
        var employees = await db.HrEmployees.AsNoTracking()
            .Where(row => row.BusinessId == businessId && employeeIds.Contains(row.Id))
            .Select(row => new { row.Id, row.EmployeeCode, row.DisplayName })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var locationIds = page.Select(row => row.OperationalLocationProjectionId).ToHashSet();
        var locations = await db.OperationalLocationProjections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && locationIds.Contains(row.Id))
            .Select(row => new { row.Id, row.Name })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var payrollRecoveries = await (
            from line in db.PayrollEmployeeResultLines.AsNoTracking()
            join result in db.PayrollEmployeeResults.AsNoTracking()
                on new { line.BusinessId, PayrollEmployeeResultId = line.PayrollEmployeeResultId }
                equals new { result.BusinessId, PayrollEmployeeResultId = result.Id }
            join run in db.PayrollRuns.AsNoTracking()
                on new { result.BusinessId, Id = result.PayrollRunId }
                equals new { run.BusinessId, run.Id }
            join period in db.PayrollPeriods.AsNoTracking()
                on new { run.BusinessId, Id = run.PayrollPeriodId }
                equals new { period.BusinessId, period.Id }
            where line.BusinessId == businessId
                && line.SourceType == "LoanRecovery"
                && result.CalculationVersion == run.CalculationVersion
                && result.Status == PayrollEmployeeResultStatuses.Calculated
                && employeeIds.Contains(result.EmployeeId)
                && period.PeriodStart <= request.To && period.PeriodEnd >= request.From
                && (request.LocationId == null || result.LocationIdSnapshot == request.LocationId)
                && (allowedLocationIds == null || result.LocationIdSnapshot.HasValue
                    && allowedLocationIds.Contains(result.LocationIdSnapshot.Value))
            select new { line.SourceId, line.Amount })
            .ToListAsync(cancellationToken);
        var payrollRecoveryByLoan = payrollRecoveries
            .Select(line => (Line: line, Separator: line.SourceId?.IndexOf(':') ?? -1))
            .Where(item => item.Separator > 0
                && long.TryParse(item.Line.SourceId![..item.Separator],
                    NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(item => new
            {
                LoanId = long.Parse(item.Line.SourceId![..item.Separator], CultureInfo.InvariantCulture),
                item.Line.Amount
            })
            .Where(item => loanIds.Contains(item.LoanId))
            .GroupBy(item => item.LoanId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        var rows = page.Select(row =>
        {
            var opening = row.IsOpeningBalance
                ? row.OpeningBalanceAmount ?? row.OutstandingAmount
                : row.ApprovedAmount ?? row.PrincipalAmount;
            var recovered = repayments.GetValueOrDefault(row.Id)
                + payrollRecoveryByLoan.GetValueOrDefault(row.Id);
            return new PayrollAdvancedReportRow(
                row.EmployeeId, employees.GetValueOrDefault(row.EmployeeId)?.EmployeeCode,
                employees.GetValueOrDefault(row.EmployeeId)?.DisplayName, null, null,
                null, null, null, null, "LoanRecovery", null, row.LoanType,
                null, null, null, row.Status, row.OperationalLocationProjectionId,
                locations.GetValueOrDefault(row.OperationalLocationProjectionId)?.Name,
                request.From, request.To, null, opening, recovered, row.OutstandingAmount,
                "Recovery includes persisted Payroll loan-recovery lines and repayment records.");
        }).ToArray();
        return new PayrollAdvancedReportPage(
            request.Kind, rows, request.Page, request.PageSize, totalCount);
    }

    private async Task<PayrollAdvancedReportPage> PaymentOutstandingAsync(
        long businessId,
        PayrollAdvancedReportQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var payments = from payment in db.PayrollPayments.AsNoTracking()
                       join run in db.PayrollRuns.AsNoTracking()
                           on new { payment.BusinessId, Id = payment.PayrollRunId }
                           equals new { run.BusinessId, Id = (long?)run.Id }
                       join period in db.PayrollPeriods.AsNoTracking()
                           on new { run.BusinessId, Id = run.PayrollPeriodId }
                           equals new { period.BusinessId, period.Id }
                       where payment.BusinessId == businessId
                           && period.PeriodStart <= request.To && period.PeriodEnd >= request.From
                           && (request.RunId == null || run.Id == request.RunId)
                           && (request.EmployeeId == null || payment.EmployeeId == request.EmployeeId)
                           && (request.LocationId == null
                               || payment.HistoricalLocationIdSnapshot == request.LocationId)
                           && !db.PayrollPayments.AsNoTracking().Any(child =>
                               child.BusinessId == businessId && child.OriginalPaymentId == payment.Id)
                       select new { Payment = payment, Run = run, Period = period };
        if (allowedLocationIds is not null)
        {
            payments = payments.Where(row =>
                row.Payment.HistoricalLocationIdSnapshot.HasValue
                && allowedLocationIds.Contains(row.Payment.HistoricalLocationIdSnapshot.Value));
        }

        var totalCount = await payments.LongCountAsync(cancellationToken);
        var page = await payments.OrderBy(row => row.Period.PeriodStart)
            .ThenBy(row => row.Run.Id)
            .ThenBy(row => row.Payment.EmployeeId)
            .ThenBy(row => row.Payment.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(row => new
            {
                PaymentId = row.Payment.Id,
                row.Payment.EmployeeId,
                row.Payment.PayableAmount,
                row.Payment.PaidAmount,
                row.Payment.OutstandingAmount,
                row.Payment.Status,
                row.Payment.ResolutionStatus,
                row.Payment.Method,
                row.Payment.HistoricalLocationIdSnapshot,
                RunId = row.Run.Id,
                row.Run.CalculationVersion,
                row.Run.RunType,
                row.Run.OriginalPayrollRunId,
                PeriodId = row.Period.Id,
                row.Period.PeriodCode,
                row.Period.PeriodStart,
                row.Period.PeriodEnd
            }).ToListAsync(cancellationToken);

        if (page.Count == 0)
            return new PayrollAdvancedReportPage(
                "PaymentOutstanding", Array.Empty<PayrollAdvancedReportRow>(),
                request.Page, request.PageSize, totalCount);

        var employeeIds = page.Select(row => row.EmployeeId).Distinct().ToHashSet();
        var employees = (await db.HrEmployees.AsNoTracking()
                .Where(row => row.BusinessId == businessId)
                .Select(row => new { row.Id, row.EmployeeCode, row.DisplayName })
                .ToListAsync(cancellationToken))
            .Where(row => employeeIds.Contains(row.Id))
            .ToDictionary(row => row.Id);
        var locationIds = page.Where(row => row.HistoricalLocationIdSnapshot.HasValue)
            .Select(row => row.HistoricalLocationIdSnapshot!.Value).Distinct().ToHashSet();
        var locations = (await db.OperationalLocationProjections.AsNoTracking()
                .Where(row => row.BusinessId == businessId)
                .Select(row => new { row.Id, row.Name })
                .ToListAsync(cancellationToken))
            .Where(row => locationIds.Contains(row.Id))
            .ToDictionary(row => row.Id);

        var paymentIds = page.Select(row => row.PaymentId).Distinct().ToList();
        var latestReconciliationRows = await (
            from reconciliation in db.PayrollPaymentReconciliations.AsNoTracking()
            join allocation in db.PayrollPaymentAllocations.AsNoTracking()
                on new { reconciliation.BusinessId, reconciliation.PayrollPaymentAllocationId }
                equals new { allocation.BusinessId, PayrollPaymentAllocationId = allocation.Id }
            where reconciliation.BusinessId == businessId
                && paymentIds.Contains(reconciliation.PayrollPaymentId)
                && !db.PayrollPaymentReconciliations.AsNoTracking().Any(newer =>
                    newer.BusinessId == reconciliation.BusinessId
                    && newer.PayrollPaymentId == reconciliation.PayrollPaymentId
                    && newer.PayrollPaymentAllocationId == reconciliation.PayrollPaymentAllocationId
                    && newer.Version > reconciliation.Version)
            select new
            {
                reconciliation.PayrollPaymentId,
                reconciliation.Status
            }).ToListAsync(cancellationToken);
        var reconciliationStatusesByPayment = latestReconciliationRows
            .GroupBy(row => row.PayrollPaymentId)
            .ToDictionary(
                group => group.Key,
                group => string.Join(", ", group.Select(row => row.Status)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(status => status, StringComparer.Ordinal)));

        var rows = page.Select(row =>
        {
            var employee = employees.GetValueOrDefault(row.EmployeeId);
            var locationName = row.HistoricalLocationIdSnapshot.HasValue
                ? locations.GetValueOrDefault(row.HistoricalLocationIdSnapshot.Value)?.Name
                : null;
            var reconciliationStatuses = reconciliationStatusesByPayment.GetValueOrDefault(row.PaymentId);
            var detail = string.IsNullOrWhiteSpace(reconciliationStatuses)
                ? $"Resolution: {row.ResolutionStatus}."
                : $"Reconciliation: {reconciliationStatuses}. Resolution: {row.ResolutionStatus}.";
            return new PayrollAdvancedReportRow(
                row.EmployeeId,
                employee?.EmployeeCode,
                employee?.DisplayName,
                row.PeriodId,
                row.PeriodCode,
                row.RunId,
                row.CalculationVersion,
                row.RunType,
                row.OriginalPayrollRunId,
                "PaymentOutstanding",
                row.Method,
                null,
                row.PayableAmount,
                row.PaidAmount,
                row.OutstandingAmount,
                row.Status,
                row.HistoricalLocationIdSnapshot,
                locationName,
                row.PeriodStart,
                row.PeriodEnd,
                null,
                null,
                null,
                row.OutstandingAmount,
                detail);
        }).ToArray();

        return new PayrollAdvancedReportPage(
            request.Kind, rows, request.Page, request.PageSize, totalCount);
    }

    private async Task<PayrollAdvancedReportPage> VarianceAsync(
        long businessId,
        PayrollAdvancedReportQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var left = VarianceTotals(businessId, request.RunId!.Value, request.EmployeeId, request.LocationId,
            allowedLocationIds);
        var right = VarianceTotals(businessId, request.CompareRunId!.Value, request.EmployeeId,
            request.LocationId, allowedLocationIds);
        var employeeIds = left.Select(row => row.EmployeeId).Union(right.Select(row => row.EmployeeId));
        var combined =
            from employeeId in employeeIds
            join before in left on employeeId equals before.EmployeeId into oldRows
            from before in oldRows.DefaultIfEmpty()
            join after in right on employeeId equals after.EmployeeId into newRows
            from after in newRows.DefaultIfEmpty()
            orderby employeeId
            select new PayrollAdvancedReportRow(
                employeeId,
                after == null ? before!.EmployeeCode : after.EmployeeCode,
                after == null ? before!.EmployeeName : after.EmployeeName,
                null, null, request.CompareRunId, null, "Variance", request.RunId,
                "RunVariance", null, null,
                (after == null ? 0m : after.Gross) - (before == null ? 0m : before.Gross),
                (after == null ? 0m : after.Deductions) - (before == null ? 0m : before.Deductions),
                (after == null ? 0m : after.Net) - (before == null ? 0m : before.Net),
                "Compared", after == null ? before!.LocationId : after.LocationId,
                null, null, null, null, null, null, null,
                $"Run {request.RunId} compared with run {request.CompareRunId}.");
        var totalCount = await combined.LongCountAsync(cancellationToken);
        var rows = await combined.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return new PayrollAdvancedReportPage(request.Kind, rows, request.Page, request.PageSize, totalCount);
    }

    private IQueryable<VarianceTotal> VarianceTotals(
        long businessId,
        long runId,
        long? employeeId,
        long? locationId,
        IReadOnlyCollection<long>? allowedLocationIds)
    {
        var query = from result in db.PayrollEmployeeResults.AsNoTracking()
                    join run in db.PayrollRuns.AsNoTracking()
                        on new { result.BusinessId, Id = result.PayrollRunId }
                        equals new { run.BusinessId, run.Id }
                    where result.BusinessId == businessId && run.Id == runId
                        && result.CalculationVersion == run.CalculationVersion
                        && result.Status == PayrollEmployeeResultStatuses.Calculated
                        && (employeeId == null || result.EmployeeId == employeeId)
                        && (locationId == null || result.LocationIdSnapshot == locationId)
                        && (allowedLocationIds == null || result.LocationIdSnapshot.HasValue
                            && allowedLocationIds.Contains(result.LocationIdSnapshot.Value))
                    group result by new
                    {
                        result.EmployeeId, result.EmployeeCodeSnapshot, result.EmployeeNameSnapshot,
                        result.LocationIdSnapshot
                    } into groupRows
                    select new VarianceTotal
                    {
                        EmployeeId = groupRows.Key.EmployeeId,
                        EmployeeCode = groupRows.Key.EmployeeCodeSnapshot,
                        EmployeeName = groupRows.Key.EmployeeNameSnapshot,
                        LocationId = groupRows.Key.LocationIdSnapshot,
                        Gross = groupRows.Sum(row => row.GrossEarnings ?? 0m),
                        Deductions = groupRows.Sum(row => row.TotalDeductions ?? 0m),
                        Net = groupRows.Sum(row => row.NetPay ?? 0m)
                    };
        return query;
    }

    private async Task<PayrollAdvancedReportPage> PageAsync(
        PayrollAdvancedReportQuery request,
        IQueryable<PayrollAdvancedReportRow> query,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return new PayrollAdvancedReportPage(request.Kind, rows, request.Page, request.PageSize, totalCount);
    }

    private async Task<IReadOnlyCollection<long>?> ResolveLocationIdsAsync(
        long businessId,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.AllowedSources is null)
            return null;

        var allowedSources = scope.AllowedSources
            .Select(source => source.EntityType + "\u001f" + source.EntityId)
            .ToHashSet(StringComparer.Ordinal);
        var sources = await db.OperationalLocationProjections.AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .Select(row => new { row.Id, row.SourceEntityType, row.SourceEntityId })
            .ToListAsync(cancellationToken);
        return sources.Where(row => allowedSources.Contains(
                row.SourceEntityType + "\u001f" + row.SourceEntityId))
            .Select(row => row.Id).ToArray();
    }

    private sealed class VarianceTotal
    {
        public long EmployeeId { get; init; }
        public string EmployeeCode { get; init; } = string.Empty;
        public string EmployeeName { get; init; } = string.Empty;
        public long? LocationId { get; init; }
        public decimal Gross { get; init; }
        public decimal Deductions { get; init; }
        public decimal Net { get; init; }
    }
}
