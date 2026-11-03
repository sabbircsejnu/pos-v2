using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollPayslipStore(RetailPOSDbContext db) : IPayrollPayslipStore
{
    private static readonly SemaphoreSlim LocalGenerationGate = new(1, 1);

    public async Task<PayrollPayslipSource> GetSourceAsync(
        long businessId, long runId, long employeeId, CancellationToken cancellationToken)
    {
        var run = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == runId, cancellationToken);
        if (run is null || !IsProtectedRun(run))
            throw new KeyNotFoundException("Approved Payroll result not found.");
        var period = await db.PayrollPeriods.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == run.PayrollPeriodId, cancellationToken)
            ?? throw new KeyNotFoundException("Approved Payroll result not found.");
        var result = await db.PayrollEmployeeResults.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.PayrollRunId == runId
                && row.CalculationVersion == run.CalculationVersion && row.EmployeeId == employeeId
                && row.Status == PayrollEmployeeResultStatuses.Calculated,
            cancellationToken)
            ?? throw new KeyNotFoundException("Approved Payroll result not found.");
        if (result.CalculationSnapshotJson is null || result.CalculationSnapshotHash is null
            || !StringComparer.Ordinal.Equals(
                Hash(result.CalculationSnapshotJson), result.CalculationSnapshotHash)
            || result.GrossEarnings is null || result.TotalDeductions is null || result.NetPay is null
            || string.IsNullOrWhiteSpace(run.CurrencyCode) || run.DecimalPrecision is not >= 0 or > 6)
            throw new InvalidDataException("Approved Payroll result evidence is incomplete or invalid.");
        var presentation = ReadHistoricalPresentation(result.CalculationSnapshotJson);

        var lines = await db.PayrollEmployeeResultLines.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollEmployeeResultId == result.Id)
            .OrderBy(row => row.CalculationOrder).ThenBy(row => row.ComponentCodeSnapshot)
            .Select(row => new PayrollPayslipLine(row.ComponentCodeSnapshot,
                row.ComponentNameSnapshot, row.LineType, row.Amount, row.CalculationOrder))
            .ToArrayAsync(cancellationToken);
        var businessName = await db.Businesses.AsNoTracking()
            .Where(row => row.Id == businessId).Select(row => row.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Approved Payroll result not found.");
        var employee = await db.HrEmployees.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == employeeId)
            .Select(row => new { row.WorkEmail })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Approved Payroll result not found.");

        var payments = await db.PayrollPayments.AsNoTracking()
            .Include(row => row.Allocations)
            .Where(row => row.BusinessId == businessId
                && row.PayrollEmployeeResultId == result.Id
                && row.Status != PayrollPaymentStatuses.Cancelled)
            .OrderByDescending(row => row.AttemptNo).ThenByDescending(row => row.Id)
            .Take(1).ToArrayAsync(cancellationToken);
        PayrollPayslipPayment? payment = null;
        if (payments.Length != 0)
        {
            var current = payments[0];
            var allocationIds = current.Allocations.Select(row => row.Id).ToList();
            var reconciliationRows = await db.PayrollPaymentReconciliations.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && allocationIds.Contains(row.PayrollPaymentAllocationId))
                .OrderBy(row => row.Version)
                .Select(row => new { row.PayrollPaymentAllocationId, row.Version, row.Status })
                .ToArrayAsync(cancellationToken);
            var reconciliationStatuses = reconciliationRows
                .GroupBy(row => row.PayrollPaymentAllocationId)
                .Select(group => group.OrderByDescending(row => row.Version).First().Status)
                .OrderBy(status => status, StringComparer.Ordinal).ToArray();
            var references = current.Allocations.Select(row => row.ExternalReference)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(MaskReference).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            payment = new PayrollPayslipPayment(
                current.Method, current.PaidAmount, current.OutstandingAmount, current.Status,
                references.Length == 0 ? null : string.Join(", ", references), reconciliationStatuses);
        }

        var snapshotHash = Hash(JsonSerializer.Serialize(new
        {
            ResultId = result.Id,
            RunId = run.Id,
            run.RunNo,
            run.RunType,
            run.CalculationVersion,
            result.CalculationSnapshotHash,
            result.EmployeeCodeSnapshot,
            result.EmployeeNameSnapshot,
            result.LocationIdSnapshot,
            result.GrossEarnings,
            result.TotalDeductions,
            result.NetPay,
            Lines = lines
        }));

        return new PayrollPayslipSource(
            businessId, businessName, run.Id, run.CalculationVersion, run.RunType,
            run.CorrectionReference ?? run.RunReference ?? $"Run {run.RunNo}",
            run.CurrencyCode!, run.DecimalPrecision!.Value, period.PeriodStart, period.PeriodEnd,
            period.PayDate, employeeId, result.EmployeeCodeSnapshot, result.EmployeeNameSnapshot,
            result.LocationIdSnapshot, presentation.Position, employee.WorkEmail ?? string.Empty, result.Id,
            result.CalculationVersion, snapshotHash, result.GrossEarnings.Value,
            result.TotalDeductions.Value, result.NetPay.Value, lines, payment,
            presentation.LocationName, presentation.LocationReference, presentation.LocationType);
    }

    private static (string? Position, string? LocationName, string? LocationReference, string? LocationType)
        ReadHistoricalPresentation(string snapshotJson)
    {
        using var document = JsonDocument.Parse(snapshotJson);
        if (!document.RootElement.TryGetProperty("employee", out var employee)
            || employee.ValueKind != JsonValueKind.Object)
            return (null, null, null, null);

        var positionCode = ReadString(employee, "positionCode");
        var positionName = ReadString(employee, "positionName");
        var position = string.IsNullOrWhiteSpace(positionCode)
            ? positionName
            : string.IsNullOrWhiteSpace(positionName)
                ? positionCode
                : $"{positionName} ({positionCode})";
        return (position,
            ReadString(employee, "locationName"),
            ReadString(employee, "locationReference"),
            ReadString(employee, "locationType"));
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    public Task<PayrollArtifactVersion?> FindLatestAsync(
        long businessId, long runId, long employeeId, CancellationToken cancellationToken) =>
        (from artifact in db.PayrollArtifactVersions.AsNoTracking()
         join result in db.PayrollEmployeeResults.AsNoTracking()
             on new { artifact.BusinessId, ResultId = artifact.PayrollEmployeeResultId }
             equals new { result.BusinessId, ResultId = result.Id }
         where artifact.BusinessId == businessId && artifact.PayrollRunId == runId
             && result.EmployeeId == employeeId && artifact.ArtifactType == "Payslip"
         orderby artifact.VersionNo descending
         select artifact).FirstOrDefaultAsync(cancellationToken);

    public Task<PayrollArtifactVersion?> FindAsync(
        long businessId, Guid artifactVersionId, CancellationToken cancellationToken) =>
        db.PayrollArtifactVersions.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == artifactVersionId, cancellationToken);

    public async Task<PayrollPayslipArtifactWriteResult> CreateOrReuseAsync(
        PayrollPayslipArtifactCandidate candidate, CancellationToken cancellationToken)
    {
        var useLocalGate = !db.Database.IsNpgsql();
        if (useLocalGate)
            await LocalGenerationGate.WaitAsync(cancellationToken);
        try
        {
            EnsurePostgresTransaction();
            await LockSourceAsync(candidate, cancellationToken);
            var existing = await db.PayrollArtifactVersions.SingleOrDefaultAsync(row =>
                row.BusinessId == candidate.BusinessId && row.PayrollRunId == candidate.RunId
                && row.RunRevision == candidate.RunRevision
                && row.PayrollEmployeeResultId == candidate.EmployeeResultId
                && row.ArtifactType == "Payslip"
                && row.PayrollSnapshotHash == candidate.PayrollSnapshotHash
                && row.PaymentInfoSnapshotHash == candidate.PaymentInfoSnapshotHash
                && row.TemplateVersion == candidate.TemplateVersion, cancellationToken);
            if (existing is not null)
                return new PayrollPayslipArtifactWriteResult(existing, true);

            var previous = await db.PayrollArtifactVersions
                .Where(row => row.BusinessId == candidate.BusinessId
                    && row.PayrollRunId == candidate.RunId
                    && row.PayrollEmployeeResultId == candidate.EmployeeResultId
                    && row.ArtifactType == "Payslip")
                .OrderByDescending(row => row.VersionNo).FirstOrDefaultAsync(cancellationToken);
            var artifact = new PayrollArtifactVersion
            {
                Id = Guid.NewGuid(),
                BusinessId = candidate.BusinessId,
                PayrollRunId = candidate.RunId,
                RunRevision = candidate.RunRevision,
                PayrollEmployeeResultId = candidate.EmployeeResultId,
                ArtifactType = "Payslip",
                VersionNo = (previous?.VersionNo ?? 0) + 1,
                PayrollSnapshotHash = candidate.PayrollSnapshotHash,
                PaymentInfoSnapshotHash = candidate.PaymentInfoSnapshotHash,
                TemplateVersion = candidate.TemplateVersion,
                Format = "PDF",
                StorageKey = candidate.StorageKey,
                EncryptionKeyReference = candidate.EncryptionKeyReference,
                ContentHash = candidate.ContentHash,
                ContentType = "application/pdf",
                ByteLength = candidate.ByteLength,
                Classification = "ConfidentialPayroll",
                ScanStatus = candidate.ScanStatus,
                CreatedByUserId = candidate.CreatedByUserId,
                CreatedAtUtc = candidate.CreatedAtUtc,
                ReplacesVersionId = previous?.Id,
                FileName = candidate.FileName
            };
            db.PayrollArtifactVersions.Add(artifact);
            await db.SaveChangesAsync(cancellationToken);
            return new PayrollPayslipArtifactWriteResult(artifact, false);
        }
        finally
        {
            if (useLocalGate)
                LocalGenerationGate.Release();
        }
    }

    public async Task<PayrollPayslipDeliveryWriteResult> AddDeliveryAsync(
        PayrollPayslipDeliveryWrite delivery, CancellationToken cancellationToken)
    {
        var useLocalGate = !db.Database.IsNpgsql();
        if (useLocalGate)
            await LocalGenerationGate.WaitAsync(cancellationToken);
        try
        {
            EnsurePostgresTransaction();
            await LockDeliveryAsync(delivery.BusinessId, delivery.RequestedByUserId,
                delivery.IdempotencyKey, cancellationToken);
            var existing = await db.PayrollPayslipDeliveries.SingleOrDefaultAsync(row =>
                row.BusinessId == delivery.BusinessId
                && row.RequestedByUserId == delivery.RequestedByUserId
                && row.IdempotencyKey == delivery.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (!existing.PayloadHash.Equals(delivery.PayloadHash, StringComparison.Ordinal))
                    throw new PayrollPayslipIdempotencyConflictException();
                return new PayrollPayslipDeliveryWriteResult(existing, true);
            }

            var row = new PayrollPayslipDelivery
            {
                BusinessId = delivery.BusinessId,
                ArtifactVersionId = delivery.ArtifactVersionId,
                RecipientSnapshot = delivery.RecipientSnapshot,
                RequestedByUserId = delivery.RequestedByUserId,
                IdempotencyKey = delivery.IdempotencyKey,
                PayloadHash = delivery.PayloadHash,
                RequestedAtUtc = delivery.RequestedAtUtc,
                Status = delivery.Status,
                ProviderReference = delivery.ProviderReference,
                FailureCode = delivery.FailureCode,
                SentAtUtc = delivery.SentAtUtc,
                Version = 1
            };
            db.PayrollPayslipDeliveries.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            return new PayrollPayslipDeliveryWriteResult(row, false);
        }
        finally
        {
            if (useLocalGate)
                LocalGenerationGate.Release();
        }
    }

    public Task<PayrollPayslipDelivery?> FindDeliveryAsync(
        long businessId, long actorUserId, string idempotencyKey,
        CancellationToken cancellationToken) =>
        db.PayrollPayslipDeliveries.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.RequestedByUserId == actorUserId
            && row.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<PayrollPayslipDelivery> UpdateDeliveryAsync(
        long deliveryId, long businessId, string status, string? providerReference,
        string? failureCode, DateTime? sentAtUtc, CancellationToken cancellationToken)
    {
        var row = await db.PayrollPayslipDeliveries.SingleOrDefaultAsync(
            item => item.BusinessId == businessId && item.Id == deliveryId, cancellationToken)
            ?? throw new KeyNotFoundException("Payslip delivery attempt not found.");
        if (row.Status != "Pending")
            throw new InvalidOperationException("Payslip delivery attempt is already terminal.");
        row.Status = status;
        row.ProviderReference = providerReference;
        row.FailureCode = failureCode;
        row.SentAtUtc = sentAtUtc;
        row.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private async Task LockSourceAsync(
        PayrollPayslipArtifactCandidate candidate, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql()) return;
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{candidate.BusinessId}:{candidate.RunId}:{candidate.EmployeeResultId}"));
        var key = BitConverter.ToInt64(keyBytes, 0);
        CryptographicOperations.ZeroMemory(keyBytes);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }

    private void EnsurePostgresTransaction()
    {
        if (db.Database.IsNpgsql() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payslip artifact and delivery writes require their audited PostgreSQL transaction.");
    }

    private async Task LockDeliveryAsync(
        long businessId, long actorUserId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql()) return;
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{businessId}:{actorUserId}:{idempotencyKey}"));
        var key = BitConverter.ToInt64(keyBytes, 0);
        CryptographicOperations.ZeroMemory(keyBytes);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }

    private static bool IsProtectedRun(PayrollRun run) =>
        run.ApprovalInstanceId is > 0 && run.DecidedByUserId is > 0 && run.DecidedAtUtc.HasValue
        && run.Status is PayrollRunStatuses.Approved or PayrollRunStatuses.PaymentInProgress
            or PayrollRunStatuses.Paid or PayrollRunStatuses.Completed;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string MaskReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return string.Empty;
        var safe = reference.Trim();
        return safe.Length <= 4 ? "****" : $"…{safe[^4..]}";
    }
}
