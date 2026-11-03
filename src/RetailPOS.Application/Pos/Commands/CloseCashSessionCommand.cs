using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos.Commands;

/// <summary>
/// Closes a Cash Session (FR-CLOSE-01..05, D2, D4). The session row is locked <c>FOR UPDATE</c> before the
/// authoritative summary is computed, so no sale or cash movement can commit after the summary was taken; a second
/// concurrent or repeated close returns the already-committed result without any new fact, audit or event.
/// </summary>
public sealed record CloseCashSessionCommand(
    long SessionId,
    decimal ActualClosingCashAmount,
    string? ClosingNotes,
    long ClosedByUserId,
    string? VarianceReason = null,
    IReadOnlyList<CashDenominationInput>? Denominations = null,
    IReadOnlyList<CashHandoverInput>? Handovers = null) : IRequest<CashSessionReadModel>;

public sealed class CloseCashSessionCommandHandler(
    ICashSessionStore store,
    ICashSessionWriteOutletAuthorizer outletAuthorizer,
    ICashSessionCloseAuditWriter auditWriter,
    ICashSessionClosedPublisher eventPublisher,
    ICashSessionEventWriter eventWriter,
    TimeProvider clock)
    : IRequestHandler<CloseCashSessionCommand, CashSessionReadModel>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CashSessionReadModel> Handle(
        CloseCashSessionCommand request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var outletId = await store.GetOutletIdAsync(request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
        await outletAuthorizer.AuthorizeWriteAsync(outletId, cancellationToken);

        var denominations = request.Denominations ?? [];
        var handovers = request.Handovers ?? [];
        CashSessionCloseEvidenceValidator.Validate(request.ActualClosingCashAmount, denominations, handovers);

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var committed = false;
        CashSession closed;
        CashDifferenceApproval? approval = null;
        try
        {
            var locked = await store.LockAsync(request.SessionId, CashSessionLockMode.Update, cancellationToken)
                ?? throw new KeyNotFoundException("Cash session not found.");

            if (locked.Status == CashSessionStatuses.Closed)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                committed = true;
                return CashSessionMapper.ToReadModel(locked, clock.GetUtcNow().UtcDateTime);
            }

            // PostgreSQL stores microseconds; truncate so the first response equals what a retry reads back.
            var rawClosingAt = clock.GetUtcNow().UtcDateTime;
            var closingAt = new DateTime(rawClosingAt.Ticks - rawClosingAt.Ticks % 10, DateTimeKind.Utc);
            var facts = await store.GetFactsAsync(locked.Id, cancellationToken)
                ?? throw new KeyNotFoundException("Cash session not found.");
            var summary = CashSessionExpectedCashCalculator.Calculate(facts, closingAt);
            var counted = request.ActualClosingCashAmount;
            var variance = counted - summary.ExpectedClosingCash;
            var reason = string.IsNullOrWhiteSpace(request.VarianceReason)
                ? request.ClosingNotes
                : request.VarianceReason;
            reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (variance != 0 && reason is null)
                throw new CashSessionException(
                    CashSessionErrorCodes.VarianceReasonRequired,
                    "A reason is required when the counted cash differs from the expected cash.");

            var notes = string.IsNullOrWhiteSpace(request.ClosingNotes) ? null : request.ClosingNotes.Trim();
            var persisted = await store.MarkClosedAsync(
                new CashSessionClosePersist(
                    locked.Id,
                    summary.ExpectedClosingCash,
                    counted,
                    variance,
                    closingAt,
                    request.ClosedByUserId,
                    notes,
                    reason),
                cancellationToken);
            if (!persisted)
                throw new CashSessionException(CashSessionErrorCodes.Closed, "The cash session is already closed.", 409);

            var handedOver = handovers.Sum(handover => handover.Amount);
            var businessId = locked.BusinessId
                ?? throw new InvalidOperationException("Cash session has no Business scope.");
            var snapshot = BuildSnapshot(locked, summary, counted, variance, reason, notes, handedOver, denominations, closingAt, request.ClosedByUserId);
            var handoverRows = handovers.Select(handover => new CashSessionHandover
            {
                BusinessId = businessId,
                OutletId = locked.OutletId,
                CashSessionId = locked.Id,
                Method = handover.Method,
                Amount = handover.Amount,
                GivenByUserId = request.ClosedByUserId,
                RecipientUserId = handover.RecipientUserId,
                RecipientName = Truncate(handover.RecipientName, 150),
                BankName = Truncate(handover.BankName, 150),
                BankAccountReference = Truncate(handover.BankAccountReference, 100),
                DepositReference = Truncate(handover.DepositReference, 100),
                DepositDate = handover.DepositDate?.Date,
                Notes = Truncate(handover.Notes, 500),
                // A deposit awaiting the bank is never labelled confirmed (FR-CLOSE-04).
                Status = handover.Method == CashHandoverMethods.BankDeposit
                    ? CashHandoverStatuses.PendingBankVerification
                    : CashHandoverStatuses.Handed,
                CreatedAtUtc = closingAt
            }).ToList();

            if (variance != 0)
            {
                approval = new CashDifferenceApproval
                {
                    BusinessId = businessId,
                    OutletId = locked.OutletId,
                    CashSessionId = locked.Id,
                    VarianceAmount = variance,
                    Reason = reason!,
                    Status = CashDifferenceStatuses.Pending,
                    PostingStatus = CashDifferencePostingStatuses.NotRequired,
                    CreatedByUserId = request.ClosedByUserId,
                    CreatedAtUtc = closingAt
                };
            }

            await store.AddCloseEvidenceAsync(snapshot, handoverRows, approval, cancellationToken);

            await eventWriter.AppendAsync(
                new CashSessionEvent(
                    CashSessionEventTypes.Closed,
                    businessId,
                    "CashSession",
                    locked.Id.ToString(),
                    "closed",
                    closingAt,
                    new
                    {
                        businessId,
                        cashSessionId = locked.Id,
                        outletId = locked.OutletId,
                        terminalId = locked.TerminalId,
                        businessDate = locked.BusinessDate.ToString("yyyy-MM-dd"),
                        closedAtUtc = closingAt,
                        closedByUserId = request.ClosedByUserId,
                        expectedCash = summary.ExpectedClosingCash,
                        countedCash = counted,
                        variance,
                        handedOverTotal = handedOver,
                        retainedAtOutlet = counted - handedOver,
                        wasOverdue = summary.IsOverdue,
                        snapshotHash = snapshot.SnapshotHash
                    }),
                cancellationToken);
            await store.SaveChangesAsync(cancellationToken);

            if (approval is not null)
            {
                await eventWriter.AppendAsync(
                    new CashSessionEvent(
                        CashSessionEventTypes.VarianceRaised,
                        businessId,
                        "CashDifferenceApproval",
                        approval.Id.ToString(),
                        "raised",
                        closingAt,
                        new
                        {
                            businessId,
                            approvalId = approval.Id,
                            cashSessionId = locked.Id,
                            outletId = locked.OutletId,
                            variance,
                            reason
                        }),
                    cancellationToken);
                await store.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            closed = locked;
            closed.Status = CashSessionStatuses.Closed;
            closed.ClosedAt = closingAt;
            closed.ClosedByUserId = request.ClosedByUserId;
            closed.ExpectedClosingCashAmount = summary.ExpectedClosingCash;
            closed.ActualClosingCashAmount = counted;
            closed.DifferenceAmount = variance;
            closed.ClosingNotes = notes;
            closed.VarianceReason = reason;
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        var closedSnapshot = CashSessionMapper.ToSnapshot(closed);
        await auditWriter.RecordClosedAsync(
            closedSnapshot,
            closed.ExpectedClosingCashAmount ?? 0m,
            closed.ActualClosingCashAmount ?? 0m,
            closed.DifferenceAmount ?? 0m,
            approval?.Id,
            cancellationToken);
        await eventPublisher.PublishClosedAsync(
            closedSnapshot,
            request.ClosedByUserId,
            closed.DifferenceAmount ?? 0m,
            cancellationToken);

        return CashSessionMapper.ToReadModel(closed, clock.GetUtcNow().UtcDateTime);
    }

    private static CashSessionCloseSnapshot BuildSnapshot(
        CashSession session,
        CashSessionCloseSummaryReadModel summary,
        decimal counted,
        decimal variance,
        string? reason,
        string? notes,
        decimal handedOver,
        IReadOnlyList<CashDenominationInput> denominations,
        DateTime closedAtUtc,
        long closedByUserId)
    {
        var snapshot = new CashSessionCloseSnapshot
        {
            CashSessionId = session.Id,
            BusinessId = session.BusinessId ?? 0,
            OutletId = session.OutletId,
            OpeningCash = summary.OpeningCash,
            CashSalesRetained = summary.CashSales,
            CashTendered = summary.CashTendered,
            ChangeGiven = summary.ChangeGiven,
            CardTotal = summary.CardSales,
            MobileTotal = summary.MobileSales,
            DrawerExpenses = summary.DrawerExpenses,
            CashRefunds = summary.CashRefunds,
            Corrections = summary.Corrections,
            ExpectedCash = summary.ExpectedClosingCash,
            CountedCash = counted,
            Variance = variance,
            VarianceReason = reason,
            HandedOverTotal = handedOver,
            RetainedAtOutlet = counted - handedOver,
            DenominationsJson = denominations.Count == 0
                ? null
                : JsonSerializer.Serialize(
                    denominations.OrderByDescending(denomination => denomination.Value)
                        .Select(denomination => new { value = denomination.Value, count = denomination.Count }),
                    Json),
            Notes = notes,
            ClosedByUserId = closedByUserId,
            ClosedAtUtc = closedAtUtc,
            TimeZoneId = session.TimeZoneIdSnapshot,
            DurationSnapshotHours = session.DurationSnapshotHours,
            WasOverdueAtClose = summary.IsOverdue
        };
        snapshot.SnapshotHash = ComputeHash(snapshot);
        return snapshot;
    }

    internal static string ComputeHash(CashSessionCloseSnapshot snapshot)
    {
        var canonical = string.Join('|',
            snapshot.CashSessionId, snapshot.BusinessId, snapshot.OutletId,
            snapshot.OpeningCash.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.CashSalesRetained.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.CardTotal.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.MobileTotal.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.DrawerExpenses.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.CashRefunds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.Corrections.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.ExpectedCash.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.CountedCash.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.HandedOverTotal.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.ClosedByUserId,
            snapshot.ClosedAtUtc.Ticks - snapshot.ClosedAtUtc.Ticks % 10);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
