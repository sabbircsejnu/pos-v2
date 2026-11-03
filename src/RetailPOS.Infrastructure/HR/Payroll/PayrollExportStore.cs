using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollExportStore(RetailPOSDbContext db) : IPayrollExportStore
{
    public Task<PayrollExportRun?> FindRunAsync(
        long businessId, long runId, CancellationToken cancellationToken) =>
        (from run in db.PayrollRuns.AsNoTracking()
         join period in db.PayrollPeriods.AsNoTracking()
             on new { run.BusinessId, PeriodId = run.PayrollPeriodId }
             equals new { period.BusinessId, PeriodId = period.Id }
         join business in db.Businesses.AsNoTracking()
             on run.BusinessId equals business.Id
         where run.BusinessId == businessId && run.Id == runId
         select new PayrollExportRun(
             run.BusinessId,
             business.Name,
             run.Id,
             period.Id,
             run.RunNo,
             run.RunType,
             run.CorrectionReference ?? run.RunReference ?? $"Run {run.RunNo}",
             run.CorrectionReference,
             run.OriginalPayrollRunId,
             run.CorrectionType,
             run.ScopeType,
             run.ScopeId,
             run.Status,
             run.CalculationVersion,
             run.CurrencyCode ?? string.Empty,
             run.DecimalPrecision ?? 2,
             period.PeriodCode,
             period.PeriodStart,
             period.PeriodEnd,
             period.PayDate))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PayrollExportEmployee>> GetEmployeesAsync(
        long businessId,
        long runId,
        int revision,
        long? primaryLocationScopeId,
        bool includePaymentData,
        CancellationToken cancellationToken)
    {
        var resultRows = await db.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.PayrollRunId == runId
                && row.CalculationVersion == revision
                && row.Status == PayrollEmployeeResultStatuses.Calculated
                && (primaryLocationScopeId == null
                    || row.LocationIdSnapshot == primaryLocationScopeId))
            .Select(row => new
            {
                row.Id,
                row.EmployeeId,
                row.EmployeeCodeSnapshot,
                row.EmployeeNameSnapshot,
                row.LocationIdSnapshot,
                row.CalculationSnapshotJson,
                row.CalculationSnapshotHash,
                row.GrossEarnings,
                row.TotalDeductions,
                row.NetPay
            })
            .ToArrayAsync(cancellationToken);

        foreach (var row in resultRows)
        {
            if (row.CalculationSnapshotJson is null || row.CalculationSnapshotHash is null
                || !StringComparer.Ordinal.Equals(
                    Hash(row.CalculationSnapshotJson), row.CalculationSnapshotHash)
                || row.GrossEarnings is null || row.TotalDeductions is null || row.NetPay is null)
                throw new InvalidDataException(
                    "An approved Payroll result contains incomplete or invalid persisted evidence.");
        }

        var resultIds = resultRows.Select(row => row.Id).ToList();
        var lines = resultIds.Count == 0
            ? Array.Empty<PayrollExportLineRow>()
            : await db.PayrollEmployeeResultLines.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && resultIds.Contains(row.PayrollEmployeeResultId))
                .OrderBy(row => row.CalculationOrder)
                .ThenBy(row => row.ComponentCodeSnapshot)
                .ThenBy(row => row.Id)
                .Select(row => new PayrollExportLineRow(
                    row.PayrollEmployeeResultId,
                    new PayrollExportComponentLine(
                        row.Id, row.ComponentCodeSnapshot, row.ComponentNameSnapshot,
                        row.LineType, row.Amount, row.CalculationOrder)))
                .ToArrayAsync(cancellationToken);

        var paymentsByResult = new Dictionary<long, PayrollExportPayment>();
        if (includePaymentData && resultIds.Count != 0)
        {
            var paymentRows = await db.PayrollPayments.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && row.PayrollEmployeeResultId != null
                    && resultIds.Contains(row.PayrollEmployeeResultId.Value)
                    && row.Status != PayrollPaymentStatuses.Cancelled)
                .OrderBy(row => row.PayrollEmployeeResultId)
                .ThenByDescending(row => row.AttemptNo)
                .ThenByDescending(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    PayrollEmployeeResultId = row.PayrollEmployeeResultId!.Value,
                    row.PaidAmount,
                    row.OutstandingAmount,
                    row.Status
                })
                .ToArrayAsync(cancellationToken);
            var latestPayments = paymentRows
                .GroupBy(row => row.PayrollEmployeeResultId)
                .Select(group => group.First())
                .ToArray();
            var paymentIds = latestPayments.Select(row => row.Id).ToList();

            if (paymentIds.Count != 0)
            {
                var allocations = await db.PayrollPaymentAllocations.AsNoTracking()
                    .Where(row => row.BusinessId == businessId
                        && paymentIds.Contains(row.PayrollPaymentId))
                    .Select(row => new { row.Id, row.PayrollPaymentId })
                    .ToArrayAsync(cancellationToken);
                var allocationIds = allocations.Select(row => row.Id).ToList();
                var reconciliationRows = allocationIds.Count == 0
                    ? Array.Empty<PayrollExportReconciliationRow>()
                    : await db.PayrollPaymentReconciliations.AsNoTracking()
                        .Where(row => row.BusinessId == businessId
                            && allocationIds.Contains(row.PayrollPaymentAllocationId))
                        .OrderBy(row => row.PayrollPaymentAllocationId)
                        .ThenByDescending(row => row.Version)
                        .ThenByDescending(row => row.Id)
                        .Select(row => new PayrollExportReconciliationRow(
                            row.PayrollPaymentAllocationId,
                            row.Status))
                        .ToArrayAsync(cancellationToken);
                var latestStatusesByAllocation = reconciliationRows
                    .GroupBy(row => row.PayrollPaymentAllocationId)
                    .ToDictionary(group => group.Key, group => group.First().Status);
                var statusesByPayment = allocations
                    .Select(row => (row.PayrollPaymentId, Status:
                        latestStatusesByAllocation.GetValueOrDefault(row.Id)))
                    .Where(row => row.Status is not null)
                    .GroupBy(row => row.PayrollPaymentId)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<string>)group.Select(row => row.Status!)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(status => status, StringComparer.Ordinal)
                            .ToArray());

                foreach (var payment in latestPayments)
                {
                    paymentsByResult[payment.PayrollEmployeeResultId] = new PayrollExportPayment(
                        payment.PaidAmount,
                        payment.OutstandingAmount,
                        payment.Status,
                        statusesByPayment.GetValueOrDefault(payment.Id) ?? []);
                }
            }
        }

        var linesById = resultIds.ToDictionary(id => id, _ => new List<PayrollExportComponentLine>());
        foreach (var line in lines)
            linesById[line.ResultId].Add(line.Line);

        return resultRows
            .Select(row =>
            {
                var presentation = ReadHistoricalPresentation(row.CalculationSnapshotJson!);
                return new PayrollExportEmployee(
                    row.EmployeeId,
                    row.EmployeeCodeSnapshot,
                    row.EmployeeNameSnapshot,
                    row.LocationIdSnapshot,
                    presentation.LocationName,
                    presentation.LocationReference,
                    presentation.LocationType,
                    presentation.PositionName,
                    presentation.PositionCode,
                    row.GrossEarnings!.Value,
                    row.TotalDeductions!.Value,
                    row.NetPay!.Value,
                    linesById[row.Id],
                    paymentsByResult.GetValueOrDefault(row.Id));
            })
            .OrderBy(row => row.EmployeeCode, StringComparer.Ordinal)
            .ThenBy(row => row.EmployeeId)
            .ToArray();
    }

    private static (string? PositionName, string? PositionCode, string? LocationName,
        string? LocationReference, string? LocationType) ReadHistoricalPresentation(string snapshotJson)
    {
        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            if (!document.RootElement.TryGetProperty("employee", out var employee)
                || employee.ValueKind != JsonValueKind.Object)
                return (null, null, null, null, null);

            return (
                ReadString(employee, "positionName"),
                ReadString(employee, "positionCode"),
                ReadString(employee, "locationName"),
                ReadString(employee, "locationReference"),
                ReadString(employee, "locationType"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "An approved Payroll result contains invalid frozen presentation evidence.", exception);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record PayrollExportLineRow(
        long ResultId, PayrollExportComponentLine Line);

    private sealed record PayrollExportReconciliationRow(
        long PayrollPaymentAllocationId, string Status);
}
