using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollFinalSettlementExecutionStore(RetailPOSDbContext db)
    : IPayrollFinalSettlementExecutionStore
{
    public async Task<IReadOnlyList<PayrollFinalSettlementAccountingStatus>>
        GetAccountingStatusAsync(
            long businessId,
            long settlementId,
            int revisionNo,
            CancellationToken cancellationToken)
    {
        var rows = await db.PayrollFinalSettlementAccountingPostings.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.FinalSettlementId == settlementId
                && row.RevisionNo == revisionNo)
            .OrderBy(row => row.PostingType)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => new PayrollFinalSettlementAccountingStatus(
                row.Id, row.RevisionNo, row.PostingType, row.PostingReference, row.Status,
                row.AccountingJournalId, row.AccountingReference, row.ErrorMessage,
                row.AttemptCount, row.Version, row.PostedAtUtc))
            .ToArray();
    }

    public Task<PayrollFinalSettlementAccountingPosting?> FindPostingAsync(
        long businessId,
        long settlementId,
        int revisionNo,
        string postingType,
        long? paymentId,
        long? paymentEvidenceId,
        CancellationToken cancellationToken) =>
        db.PayrollFinalSettlementAccountingPostings.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId
            && row.FinalSettlementId == settlementId
            && row.RevisionNo == revisionNo
            && row.PostingType == postingType
            && row.PaymentId == paymentId
            && row.PaymentEvidenceId == paymentEvidenceId,
            cancellationToken);

    public async Task LockPostingIdentityAsync(
        long businessId, string postingReference, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Final Settlement Accounting writes require the audited transaction boundary.");
        if (!db.Database.IsNpgsql())
            return;
        var identity = Encoding.UTF8.GetBytes($"{businessId}:{postingReference}");
        var hash = SHA256.HashData(identity);
        var lockId = BitConverter.ToInt64(hash, 0);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockId})", cancellationToken);
    }

    public async Task SavePostingAsync(
        PayrollFinalSettlementAccountingPosting posting,
        CancellationToken cancellationToken)
    {
        if (posting.Id == 0)
            db.PayrollFinalSettlementAccountingPostings.Add(posting);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollFinalSettlementExecutionConflictException(
                "The Final Settlement Accounting attempt changed concurrently.", exception);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollFinalSettlementExecutionConflictException(
                "The Final Settlement Accounting identity conflicts with an existing attempt.",
                exception);
        }
    }

    public async Task<PayrollFinalSettlementPaymentAccountingSource?> GetPaymentSourceAsync(
        long businessId,
        long paymentId,
        long evidenceId,
        CancellationToken cancellationToken)
    {
        var payment = await db.PayrollPayments.AsNoTracking()
            .Include(row => row.Allocations)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == paymentId,
                cancellationToken);
        var evidence = await db.PayrollPaymentEvidence.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.PayrollPaymentId == paymentId && row.Id == evidenceId,
                cancellationToken);
        if (payment?.FinalSettlementId is not long settlementId
            || payment.FinalSettlementRevisionId is not long revisionId
            || evidence is null)
            return null;

        var approvedEvidence = await (
            from header in db.PayrollFinalSettlements.AsNoTracking()
            join revision in db.PayrollFinalSettlementRevisions.AsNoTracking()
                on new { header.BusinessId, FinalSettlementId = header.Id }
                equals new { revision.BusinessId, revision.FinalSettlementId }
            join submission in db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking()
                on new { revision.BusinessId, RevisionId = revision.Id }
                equals new { submission.BusinessId, submission.RevisionId }
            where header.BusinessId == businessId
                && header.Id == settlementId
                && header.CurrentRevisionNo == payment.FinalSettlementRevisionNo
                && revision.Id == revisionId
                && submission.ApprovalInstanceId != null
                && db.ApprovalInstances.Any(instance =>
                    instance.BusinessId == businessId
                    && instance.Id == submission.ApprovalInstanceId.Value
                    && instance.ProcessType == ApprovalProcessType.FinalSettlement
                    && instance.Status == ApprovalInstanceStatus.Approved)
            select new
            {
                revision.RevisionNo,
                revision.DecimalPrecision,
                revision.RoundingMode,
                revision.CurrencyCode,
                submission.SubmittedEvidenceHash
            }).SingleOrDefaultAsync(cancellationToken);
        if (approvedEvidence is null
            || payment.FinalSettlementEvidenceHash != approvedEvidence.SubmittedEvidenceHash
            || evidence.Type != PayrollPaymentEvidenceTypes.Settlement)
            return null;

        using var document = JsonDocument.Parse(evidence.AllocationEvidenceJson);
        var evidenceAmounts = document.RootElement.EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("AllocationNo").GetInt32(),
                row => (Method: row.GetProperty("Method").GetString()
                    ?? throw new InvalidDataException("Settlement evidence has no allocation method."),
                    Amount: row.GetProperty("Amount").GetDecimal()));
        var allocationIds = payment.Allocations.Select(row => row.Id).ToArray();
        var latestReconciliations = allocationIds.Length == 0
            ? []
            : await db.PayrollPaymentReconciliations.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && allocationIds.Contains(row.PayrollPaymentAllocationId))
                .OrderBy(row => row.PayrollPaymentAllocationId)
                .ThenByDescending(row => row.Version)
                .ThenByDescending(row => row.Id)
                .ToArrayAsync(cancellationToken);
        var latestByAllocation = latestReconciliations
            .GroupBy(row => row.PayrollPaymentAllocationId)
            .ToDictionary(group => group.Key, group => group.First());
        var allocations = payment.Allocations
            .Where(row => evidenceAmounts.ContainsKey(row.AllocationNo))
            .OrderBy(row => row.AllocationNo)
            .Select(row =>
            {
                var amount = evidenceAmounts[row.AllocationNo];
                latestByAllocation.TryGetValue(row.Id, out var reconciliation);
                return new PayrollFinalSettlementPaymentAllocationSource(
                    row.Id, row.AllocationNo, row.Method, row.Amount, row.SettledAmount,
                    row.State, amount.Amount,
                    amount.Method == row.Method
                        && reconciliation?.Status == PayrollPaymentReconciliationStatuses.Reconciled
                        && reconciliation.ExpectedAmount == row.Amount
                        && reconciliation.SettledAmount == row.Amount
                        && reconciliation.EvidenceSource == row.Method);
            }).ToArray();
        if (allocations.Length != evidenceAmounts.Count)
            throw new InvalidDataException(
                "Settlement evidence references an allocation that is not attached to its payment.");

        return new PayrollFinalSettlementPaymentAccountingSource(
            settlementId, approvedEvidence.RevisionNo, revisionId, payment.EmployeeId,
            payment.HistoricalLocationIdSnapshot, approvedEvidence.DecimalPrecision,
            approvedEvidence.RoundingMode, approvedEvidence.CurrencyCode,
            approvedEvidence.SubmittedEvidenceHash, payment.Id, payment.Status,
            payment.ResolutionStatus, payment.PayableAmount, payment.PaidAmount,
            payment.OutstandingAmount, evidence.Id, evidence.Type, evidence.EvidenceDate,
            evidence.EvidenceReference, evidence.PayloadHash, allocations);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation }
        || exception.InnerException?.Message.Contains(
            "UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed class PayrollFinalSettlementExecutionConflictException(
    string message, Exception? innerException = null) : InvalidOperationException(message, innerException);
