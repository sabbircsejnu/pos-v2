using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollDashboardStore(RetailPOSDbContext db) : IPayrollDashboardStore
{
    private static readonly string[] PaymentStates =
    [
        PayrollPaymentStatuses.Pending, PayrollPaymentStatuses.Ready, PayrollPaymentStatuses.Processing,
        PayrollPaymentStatuses.PartiallyPaid, PayrollPaymentStatuses.Paid, PayrollPaymentStatuses.Held,
        PayrollPaymentStatuses.Failed, PayrollPaymentStatuses.Returned, PayrollPaymentStatuses.Cancelled
    ];

    private static readonly string[] RunStates =
    [
        PayrollRunStatuses.Draft, PayrollRunStatuses.PendingApproval, PayrollRunStatuses.Approved,
        PayrollRunStatuses.Rejected, PayrollRunStatuses.PaymentInProgress, PayrollRunStatuses.Paid,
        PayrollRunStatuses.Completed, PayrollRunStatuses.Cancelled
    ];

    private static readonly string[] PostingStates =
    [
        PayrollAccountingPostingStatuses.Pending, PayrollAccountingPostingStatuses.Processing,
        PayrollAccountingPostingStatuses.Posted, PayrollAccountingPostingStatuses.Failed,
        PayrollAccountingPostingStatuses.Conflict
    ];

    private static readonly string[] ReconciliationStates =
    [
        PayrollPaymentReconciliationStatuses.Reconciled,
        PayrollPaymentReconciliationStatuses.PartiallyReconciled,
        PayrollPaymentReconciliationStatuses.Unreconciled,
        PayrollPaymentReconciliationStatuses.Exception
    ];

    public async Task<PayrollDashboardSummary> GetAsync(
        long businessId,
        GetPayrollDashboardQuery request,
        HrOperationalLocationReadScope locationScope,
        CancellationToken cancellationToken)
    {
        if (locationScope.BusinessId != businessId)
            throw new UnauthorizedAccessException("Payroll dashboard scope is outside Business.");

        var allowedLocationIds = await ResolveLocationIdsAsync(
            businessId, locationScope, cancellationToken);
        if (request.LocationId is long locationId
            && allowedLocationIds is not null && !allowedLocationIds.Contains(locationId))
            throw new UnauthorizedAccessException("The Payroll dashboard location is outside the caller's scope.");

        var selectedRun = request.RunId is long runId
            ? await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == runId, cancellationToken)
                ?? throw new KeyNotFoundException("Payroll run was not found.")
            : null;

        if (selectedRun is not null
            && (request.PeriodId is long selectedPeriodId && selectedRun.PayrollPeriodId != selectedPeriodId
                || request.RunType is not null && selectedRun.RunType != request.RunType))
            throw new KeyNotFoundException("Payroll run was not found.");

        var periodId = selectedRun?.PayrollPeriodId ?? request.PeriodId;
        var periods = db.PayrollPeriods.AsNoTracking().Where(row => row.BusinessId == businessId);
        if (periodId is long requestedPeriodId)
        {
            periods = periods.Where(row => row.Id == requestedPeriodId);
        }
        else if (request.FromDate is DateOnly from && request.ToDate is DateOnly to)
        {
            periods = periods.Where(row => row.PeriodStart <= to && row.PeriodEnd >= from);
        }

        var period = await periods.OrderByDescending(row => row.PeriodStart)
            .ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (periodId is not null && period is null)
            throw new KeyNotFoundException("Payroll period was not found.");
        if (selectedRun is not null && period is not null
            && (request.FromDate is DateOnly runFrom && period.PeriodEnd < runFrom
                || request.ToDate is DateOnly runTo && period.PeriodStart > runTo))
            throw new KeyNotFoundException("Payroll run was not found.");

        var visibleRuns = db.PayrollRuns.AsNoTracking().Where(row =>
            row.BusinessId == businessId && period != null && row.PayrollPeriodId == period.Id);
        if (request.RunType is not null)
            visibleRuns = visibleRuns.Where(row => row.RunType == request.RunType);
        if (allowedLocationIds is not null)
        {
            visibleRuns = visibleRuns.Where(run =>
                (run.ScopeType == PayrollRunScopeTypes.Location
                    && run.ScopeId.HasValue && allowedLocationIds.Contains(run.ScopeId.Value))
                || db.PayrollEmployeeResults.AsNoTracking().Any(result =>
                    result.BusinessId == businessId && result.PayrollRunId == run.Id
                    && result.LocationIdSnapshot.HasValue
                    && allowedLocationIds.Contains(result.LocationIdSnapshot.Value)));
        }

        if (selectedRun is not null)
            visibleRuns = visibleRuns.Where(row => row.Id == selectedRun.Id);

        var selectedRunRow = selectedRun is null
            ? await visibleRuns.OrderByDescending(row => row.CreatedAtUtc)
                .ThenByDescending(row => row.Id).FirstOrDefaultAsync(cancellationToken)
            : await visibleRuns.SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Payroll run was not found.");

        var processingStates = period is null
            ? []
            : await FillRunStatesAsync(visibleRuns, cancellationToken);
        var runTypes = period is null
            ? []
            : await FillRunTypesAsync(visibleRuns, cancellationToken);

        var resultQuery = selectedRunRow is null
            ? db.PayrollEmployeeResults.AsNoTracking().Where(_ => false)
            : db.PayrollEmployeeResults.AsNoTracking().Where(row =>
                row.BusinessId == businessId && row.PayrollRunId == selectedRunRow.Id
                && row.Status == PayrollEmployeeResultStatuses.Calculated);
        if (allowedLocationIds is not null)
            resultQuery = resultQuery.Where(row => row.LocationIdSnapshot.HasValue
                && allowedLocationIds.Contains(row.LocationIdSnapshot.Value));
        if (request.LocationId is long filteredLocationId)
            resultQuery = resultQuery.Where(row => row.LocationIdSnapshot == filteredLocationId);

        var resultTotals = await resultQuery.GroupBy(_ => 1).Select(group => new
        {
            Count = group.LongCount(),
            Gross = group.Sum(row => row.GrossEarnings ?? 0m),
            Deductions = group.Sum(row => row.TotalDeductions ?? 0m),
            Net = group.Sum(row => row.NetPay ?? 0m)
        }).FirstOrDefaultAsync(cancellationToken);
        var employeeCount = resultTotals?.Count ?? 0;
        var gross = resultTotals?.Gross ?? 0m;
        var deductions = resultTotals?.Deductions ?? 0m;
        var netPayable = resultTotals?.Net ?? 0m;

        var payments = selectedRunRow is null
            ? db.PayrollPayments.AsNoTracking().Where(_ => false)
            : db.PayrollPayments.AsNoTracking().Where(row =>
                row.BusinessId == businessId && row.PayrollRunId == selectedRunRow.Id);
        if (allowedLocationIds is not null)
        {
            payments = payments.Where(payment =>
                payment.HistoricalLocationIdSnapshot.HasValue
                    && allowedLocationIds.Contains(payment.HistoricalLocationIdSnapshot.Value)
                || payment.PayrollEmployeeResultId.HasValue
                    && db.PayrollEmployeeResults.AsNoTracking().Any(result =>
                        result.BusinessId == businessId
                        && result.Id == payment.PayrollEmployeeResultId.Value
                        && result.LocationIdSnapshot.HasValue
                        && allowedLocationIds.Contains(result.LocationIdSnapshot.Value)));
        }
        if (request.LocationId is long paymentLocationId)
        {
            payments = payments.Where(payment =>
                payment.HistoricalLocationIdSnapshot == paymentLocationId
                || payment.PayrollEmployeeResultId.HasValue
                    && db.PayrollEmployeeResults.AsNoTracking().Any(result =>
                        result.BusinessId == businessId
                        && result.Id == payment.PayrollEmployeeResultId.Value
                        && result.LocationIdSnapshot == paymentLocationId));
        }

        var paidTotal = await payments.SumAsync(row => (decimal?)row.PaidAmount, cancellationToken) ?? 0m;
        var leafPayments = payments.Where(payment => !db.PayrollPayments.AsNoTracking().Any(child =>
            child.BusinessId == businessId && child.OriginalPaymentId == payment.Id));
        var paymentStates = await FillPaymentStatesAsync(leafPayments, cancellationToken);
        var paymentMethods = await (
                from allocation in db.PayrollPaymentAllocations.AsNoTracking()
                join payment in payments on new { allocation.BusinessId, Id = allocation.PayrollPaymentId }
                    equals new { payment.BusinessId, Id = payment.Id }
                group allocation by allocation.Method into methodGroup
                select new { Method = methodGroup.Key, Settled = methodGroup.Sum(row => row.SettledAmount) })
            .OrderBy(row => row.Method)
            .ToListAsync(cancellationToken);

        var reconciliationRows = await (
                from reconciliation in db.PayrollPaymentReconciliations.AsNoTracking()
                join payment in payments on new { reconciliation.BusinessId, Id = reconciliation.PayrollPaymentId }
                    equals new { payment.BusinessId, Id = payment.Id }
                where !db.PayrollPaymentReconciliations.AsNoTracking().Any(newer =>
                    newer.BusinessId == reconciliation.BusinessId
                    && newer.PayrollPaymentId == reconciliation.PayrollPaymentId
                    && newer.PayrollPaymentAllocationId == reconciliation.PayrollPaymentAllocationId
                    && newer.Version > reconciliation.Version)
                group reconciliation by reconciliation.Status into statusGroup
                select new { Status = statusGroup.Key, Count = statusGroup.LongCount() })
            .ToListAsync(cancellationToken);

        var accountingEnabled = await db.BusinessFeatureSettings.AsNoTracking().AnyAsync(setting =>
            setting.BusinessId == businessId && setting.FeatureKey == "accounting.enabled"
            && setting.IsEnabled, cancellationToken);

        var visibleRunIds = visibleRuns.Select(row => row.Id);
        var expensePostings = accountingEnabled
            ? await GetPayrollPostingMetricsAsync(
                businessId, period, visibleRunIds, cancellationToken)
            : [];
        var finalSettlementIds = GetFinalSettlementIds(
            businessId, period, request, allowedLocationIds);
        var finalSettlementMetrics = await GetFinalSettlementMetricsAsync(
            businessId, finalSettlementIds, cancellationToken);
        var settlementPostingMetrics = accountingEnabled
            ? await GetSettlementPostingMetricsAsync(
                businessId, period, finalSettlementIds, visibleRunIds, cancellationToken)
            : [];

        var allPostingMetrics = expensePostings.Concat(settlementPostingMetrics).ToArray();
        var accountingBlockers = allPostingMetrics
            .Where(row => row.Status != PayrollAccountingPostingStatuses.Posted)
            .Sum(row => row.Count);
        var pendingReconciliation = reconciliationRows
            .Where(row => row.Status is PayrollPaymentReconciliationStatuses.Unreconciled
                or PayrollPaymentReconciliationStatuses.PartiallyReconciled
                or PayrollPaymentReconciliationStatuses.Exception)
            .Sum(row => row.Count);
        var unresolvedPaymentCount = await leafPayments.LongCountAsync(payment =>
            payment.OutstandingAmount > 0
            && payment.Status != PayrollPaymentStatuses.Cancelled, cancellationToken);
        var partialPaymentCount = await leafPayments.LongCountAsync(payment =>
            payment.Status == PayrollPaymentStatuses.PartiallyPaid, cancellationToken);
        var correctionPendingCount = await visibleRuns.LongCountAsync(run =>
            run.RunType == PayrollRunTypes.Correction
            && (run.Status == PayrollRunStatuses.Draft
                || run.Status == PayrollRunStatuses.PendingApproval
                || run.Status == PayrollRunStatuses.Rejected), cancellationToken);
        var paymentMetrics = paymentStates;

        return new PayrollDashboardSummary(
            period is null
                ? new PayrollDashboardPeriodSummary(null, null, null, null, null)
                : new PayrollDashboardPeriodSummary(period.Id, period.PeriodCode, period.Status,
                    period.PeriodStart, period.PeriodEnd),
            new PayrollDashboardRunSummary(
                selectedRunRow?.Id,
                selectedRunRow?.Status,
                selectedRunRow?.RunType,
                employeeCount,
                gross,
                deductions,
                netPayable,
                selectedRunRow?.Status is PayrollRunStatuses.Approved
                    or PayrollRunStatuses.PaymentInProgress or PayrollRunStatuses.Paid
                    or PayrollRunStatuses.Completed ? netPayable : 0m,
                processingStates,
                runTypes,
                correctionPendingCount),
            new PayrollDashboardPaymentSummary(
                netPayable,
                paidTotal,
                Math.Max(0m, netPayable - paidTotal),
                paymentMetrics,
                GetStateCount(paymentMetrics, PayrollPaymentStatuses.Held),
                GetStateCount(paymentMetrics, PayrollPaymentStatuses.Processing),
                GetStateCount(paymentMetrics, PayrollPaymentStatuses.Failed),
                await payments.LongCountAsync(row => row.Status == PayrollPaymentStatuses.Returned,
                    cancellationToken),
                GetStateCount(paymentMetrics, PayrollPaymentStatuses.Paid),
                paymentMethods.Select(row =>
                    new PayrollDashboardMethodMetric(row.Method, row.Settled)).ToArray()),
            new PayrollDashboardReconciliationSummary(
                reconciliationRows.Count > 0,
                GetStateCount(reconciliationRows.Select(row => (row.Status, row.Count)),
                    PayrollPaymentReconciliationStatuses.Reconciled),
                GetStateCount(reconciliationRows.Select(row => (row.Status, row.Count)),
                    PayrollPaymentReconciliationStatuses.PartiallyReconciled),
                GetStateCount(reconciliationRows.Select(row => (row.Status, row.Count)),
                    PayrollPaymentReconciliationStatuses.Unreconciled),
                GetStateCount(reconciliationRows.Select(row => (row.Status, row.Count)),
                    PayrollPaymentReconciliationStatuses.Exception)),
            new PayrollDashboardAccountingSummary(
                accountingEnabled,
                expensePostings,
                settlementPostingMetrics,
                accountingBlockers),
            finalSettlementMetrics,
            new PayrollDashboardBlockerSummary(
                unresolvedPaymentCount,
                partialPaymentCount,
                pendingReconciliation,
                accountingBlockers,
                correctionPendingCount));
    }

    private async Task<IReadOnlyList<PayrollDashboardMetric>> FillRunStatesAsync(
        IQueryable<PayrollRun> runs, CancellationToken cancellationToken)
    {
        var grouped = await runs.GroupBy(row => row.Status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        return FillStates(RunStates, grouped.Select(row => (row.Status, row.Count)));
    }

    private static async Task<IReadOnlyList<PayrollDashboardMetric>> FillRunTypesAsync(
        IQueryable<PayrollRun> runs, CancellationToken cancellationToken)
    {
        var grouped = await runs.GroupBy(row => row.RunType)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        return FillStates(
            [PayrollRunTypes.Regular, PayrollRunTypes.Correction, PayrollRunTypes.OffCycle],
            grouped.Select(row => (row.Status, row.Count)));
    }

    private static async Task<IReadOnlyList<PayrollDashboardMetric>> FillPaymentStatesAsync(
        IQueryable<PayrollPayment> payments, CancellationToken cancellationToken)
    {
        var grouped = await payments.GroupBy(row => row.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.LongCount(),
                Amount = group.Sum(row => row.OutstandingAmount)
            })
            .ToListAsync(cancellationToken);
        return FillStates(PaymentStates, grouped.Select(row =>
            (row.Status, row.Count, row.Amount)));
    }

    private async Task<IReadOnlyList<PayrollDashboardPostingMetric>> GetPayrollPostingMetricsAsync(
        long businessId,
        RetailPOS.Core.Entities.HR.Payroll.PayrollPeriod? period,
        IQueryable<long> visibleRunIds,
        CancellationToken cancellationToken)
    {
        if (period is null)
            return [];

        var grouped = await db.PayrollAccountingPostings.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.PayrollPeriodId == period.Id
                && visibleRunIds.Contains(row.PayrollRunId)
                && (row.PostingType == PayrollAccountingPostingTypes.PayrollExpense
                    || row.PostingType == PayrollAccountingPostingTypes.PayrollCorrection))
            .GroupBy(row => new { row.PostingType, row.Status })
            .Select(group => new
            {
                group.Key.PostingType,
                group.Key.Status,
                Count = group.LongCount()
            })
            .ToListAsync(cancellationToken);
        return FillPostingStates(
            [PayrollAccountingPostingTypes.PayrollExpense, PayrollAccountingPostingTypes.PayrollCorrection],
            grouped.Select(row => (row.PostingType, row.Status, row.Count)));
    }

    private async Task<IReadOnlyList<PayrollDashboardPostingMetric>> GetSettlementPostingMetricsAsync(
        long businessId,
        RetailPOS.Core.Entities.HR.Payroll.PayrollPeriod? period,
        IQueryable<long> finalSettlementIds,
        IQueryable<long> visibleRunIds,
        CancellationToken cancellationToken)
    {
        var payrollSettlements = period is null
            ? []
            : await db.PayrollAccountingPostings.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && row.PayrollPeriodId == period.Id
                    && visibleRunIds.Contains(row.PayrollRunId)
                    && row.PostingType == PayrollAccountingPostingTypes.SalaryPayment)
                .GroupBy(row => row.Status)
                .Select(group => new
                {
                    PostingType = PayrollAccountingPostingTypes.SalaryPayment,
                    Status = group.Key,
                    Count = group.LongCount()
                })
                .ToListAsync(cancellationToken);
        var finalSettlementsGrouped = await db.PayrollFinalSettlementAccountingPostings.AsNoTracking()
            .Where(posting => posting.BusinessId == businessId
                && finalSettlementIds.Contains(posting.FinalSettlementId))
            .GroupBy(posting => new { posting.PostingType, posting.Status })
            .Select(postingGroup => new
            {
                postingGroup.Key.PostingType,
                postingGroup.Key.Status,
                Count = postingGroup.LongCount()
            })
            .ToListAsync(cancellationToken);

        return FillPostingStates(
            [PayrollAccountingPostingTypes.SalaryPayment,
                PayrollFinalSettlementPostingTypes.Expense,
                PayrollFinalSettlementPostingTypes.Receivable,
                PayrollFinalSettlementPostingTypes.Payment],
            payrollSettlements.Concat(finalSettlementsGrouped)
                .Select(row => (row.PostingType, row.Status, row.Count)));
    }

    private IQueryable<long> GetFinalSettlementIds(
        long businessId,
        RetailPOS.Core.Entities.HR.Payroll.PayrollPeriod? period,
        GetPayrollDashboardQuery request,
        IReadOnlyCollection<long>? allowedLocationIds)
    {
        var rangeStart = request.FromDate ?? period?.PeriodStart;
        var rangeEnd = request.ToDate ?? period?.PeriodEnd;
        return db.PayrollFinalSettlements.AsNoTracking()
            .Where(settlement => settlement.BusinessId == businessId
                && db.PayrollFinalSettlementRevisions.AsNoTracking().Any(revision =>
                    revision.BusinessId == settlement.BusinessId
                    && revision.FinalSettlementId == settlement.Id
                    && revision.RevisionNo == settlement.CurrentRevisionNo
                    && (rangeStart == null || revision.SettlementDate >= rangeStart.Value)
                    && (rangeEnd == null || revision.SettlementDate <= rangeEnd.Value))
                && db.HrExitCases.AsNoTracking().Any(exit =>
                    exit.BusinessId == settlement.BusinessId
                    && exit.Id == settlement.ExitCaseId
                    && (request.LocationId == null
                        || exit.PrimaryLocationProjectionIdSnapshot == request.LocationId.Value)
                    && (allowedLocationIds == null
                        || exit.PrimaryLocationProjectionIdSnapshot.HasValue
                            && allowedLocationIds.Contains(exit.PrimaryLocationProjectionIdSnapshot.Value))))
            .Select(settlement => settlement.Id);
    }

    private async Task<PayrollDashboardFinalSettlementSummary> GetFinalSettlementMetricsAsync(
        long businessId,
        IQueryable<long> settlementIds,
        CancellationToken cancellationToken)
    {
        var approved =
            from settlement in db.PayrollFinalSettlements.AsNoTracking()
            join revision in db.PayrollFinalSettlementRevisions.AsNoTracking()
                on new { settlement.BusinessId, Id = settlement.Id, settlement.CurrentRevisionNo }
                equals new { revision.BusinessId, Id = revision.FinalSettlementId,
                    CurrentRevisionNo = revision.RevisionNo }
            where settlementIds.Contains(settlement.Id)
                && db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking().Any(submission =>
                    submission.BusinessId == settlement.BusinessId
                    && submission.FinalSettlementId == settlement.Id
                    && submission.RevisionNo == revision.RevisionNo
                    && submission.ApprovalInstanceId.HasValue
                    && db.ApprovalInstances.AsNoTracking().Any(instance =>
                        instance.BusinessId == submission.BusinessId
                        && instance.Id == submission.ApprovalInstanceId.Value
                        && instance.Status == ApprovalInstanceStatus.Approved))
            select new
            {
                settlement.BusinessId,
                SettlementId = settlement.Id,
                RevisionNo = revision.RevisionNo,
                revision.NetSettlement
            };
        var positiveTotals = await approved.Where(row => row.NetSettlement > 0m)
            .Select(row => new
            {
                row.NetSettlement,
                Paid = db.PayrollPayments.AsNoTracking()
                    .Where(payment => payment.BusinessId == businessId
                        && payment.FinalSettlementId == row.SettlementId
                        && payment.FinalSettlementRevisionNo == row.RevisionNo)
                    .Sum(payment => (decimal?)payment.PaidAmount) ?? 0m
            })
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.LongCount(),
                Amount = group.Sum(row => row.NetSettlement),
                Paid = group.Sum(row => row.Paid),
                Outstanding = group.Sum(row =>
                    row.NetSettlement > row.Paid ? row.NetSettlement - row.Paid : 0m)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var negativeTotals = await approved.Where(row => row.NetSettlement < 0m)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.LongCount(),
                Amount = group.Sum(row => -row.NetSettlement)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var completedPositiveCount = await approved.Where(row => row.NetSettlement > 0m
                && (db.PayrollPayments.AsNoTracking()
                .Where(payment => payment.BusinessId == businessId
                    && payment.FinalSettlementId == row.SettlementId
                    && payment.FinalSettlementRevisionNo == row.RevisionNo)
                .Sum(payment => (decimal?)payment.PaidAmount) ?? 0m) >= row.NetSettlement)
            .LongCountAsync(cancellationToken);
        var completedNegativeCount = await approved.Where(row => row.NetSettlement < 0m)
            .LongCountAsync(row => db.PayrollFinalSettlementAccountingPostings.AsNoTracking().Any(posting =>
                posting.BusinessId == row.BusinessId
                && posting.FinalSettlementId == row.SettlementId
                && posting.RevisionNo == row.RevisionNo
                && posting.PostingType == PayrollFinalSettlementPostingTypes.Receivable
                && posting.Status == PayrollAccountingPostingStatuses.Posted), cancellationToken);

        return new PayrollDashboardFinalSettlementSummary(
            positiveTotals?.Count ?? 0,
            positiveTotals?.Amount ?? 0m,
            positiveTotals?.Paid ?? 0m,
            positiveTotals?.Outstanding ?? 0m,
            negativeTotals?.Count ?? 0,
            negativeTotals?.Amount ?? 0m,
            completedPositiveCount + completedNegativeCount,
            Math.Max(0, (positiveTotals?.Count ?? 0) - completedPositiveCount)
                + Math.Max(0, (negativeTotals?.Count ?? 0) - completedNegativeCount));
    }

    private static IReadOnlyList<PayrollDashboardPostingMetric> FillPostingStates(
        IReadOnlyList<string> categories,
        IEnumerable<(string PostingType, string Status, long Count)> grouped)
    {
        var rows = grouped.ToArray();
        return categories.SelectMany(category => PostingStates.Select(status =>
                new PayrollDashboardPostingMetric(category, status,
                    rows.Where(row => row.PostingType == category && row.Status == status)
                        .Sum(row => row.Count))))
            .ToArray();
    }

    private static IReadOnlyList<PayrollDashboardMetric> FillStates(
        IReadOnlyList<string> states,
        IEnumerable<(string Status, long Count)> grouped)
    {
        var rows = grouped.ToArray();
        return states.Select(status => new PayrollDashboardMetric(
            status, rows.Where(row => row.Status == status).Sum(row => row.Count))).ToArray();
    }

    private static IReadOnlyList<PayrollDashboardMetric> FillStates(
        IReadOnlyList<string> states,
        IEnumerable<(string Status, long Count, decimal Amount)> grouped)
    {
        var rows = grouped.ToArray();
        return states.Select(status => new PayrollDashboardMetric(
            status,
            rows.Where(row => row.Status == status).Sum(row => row.Count),
            rows.Where(row => row.Status == status).Sum(row => row.Amount))).ToArray();
    }

    private static long GetStateCount(
        IReadOnlyList<PayrollDashboardMetric> metrics, string status) =>
        metrics.FirstOrDefault(row => row.Status == status)?.Count ?? 0;

    private static long GetStateCount(
        IEnumerable<(string Status, long Count)> metrics, string status) =>
        metrics.Where(row => row.Status == status).Sum(row => row.Count);

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
        return (await db.OperationalLocationProjections.AsNoTracking()
                .Where(row => row.BusinessId == businessId)
                .Select(row => new { row.Id, row.SourceEntityType, row.SourceEntityId })
                .ToListAsync(cancellationToken))
            .Where(row => allowedSources.Contains(row.SourceEntityType + "\u001f" + row.SourceEntityId))
            .Select(row => row.Id)
            .ToArray();
    }

}
