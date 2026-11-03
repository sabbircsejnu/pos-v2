using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos.Commands;

/// <summary>
/// D2 decision step: an authorised reviewer who is not the closing user approves or rejects the variance. Approval
/// only schedules the journal; nothing is posted inside this transaction and the closed session is never touched.
/// </summary>
public sealed record ReviewCashDifferenceCommand(
    long ApprovalId,
    bool Approve,
    string? Notes,
    long ReviewerUserId) : ICommand<CashDifferenceApprovalReadModelResult>;

public sealed record CashDifferenceApprovalReadModelResult(CashDifferenceApproval Approval, bool Replayed);

public sealed class ReviewCashDifferenceCommandHandler(
    ICashDifferenceStore differences,
    ICashSessionStore sessions,
    ICashSessionOutletAuthorizer outletAuthorizer,
    ICashSessionEventWriter eventWriter,
    ICashSessionAuditWriter auditWriter,
    TimeProvider clock) : IRequestHandler<ReviewCashDifferenceCommand, CashDifferenceApprovalReadModelResult>
{
    public async Task<CashDifferenceApprovalReadModelResult> Handle(
        ReviewCashDifferenceCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await differences.FindAsync(request.ApprovalId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash difference was not found.");
        await outletAuthorizer.AuthorizeReadAsync(existing.OutletId, cancellationToken);

        if (!request.Approve && string.IsNullOrWhiteSpace(request.Notes))
            throw new CashSessionException(
                CashSessionErrorCodes.VarianceReasonRequired,
                "A reason is required to reject a cash difference.");

        await using var transaction = await sessions.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var approval = await differences.LockApprovalAsync(request.ApprovalId, cancellationToken)
                ?? throw new KeyNotFoundException("Cash difference was not found.");

            var wanted = request.Approve ? CashDifferenceStatuses.Approved : CashDifferenceStatuses.Rejected;
            if (approval.Status != CashDifferenceStatuses.Pending)
            {
                // A retry of the same decision by the same reviewer is a replay; anything else is a conflict.
                await transaction.RollbackAsync(CancellationToken.None);
                committed = true;
                if (approval.Status == wanted && approval.ReviewedByUserId == request.ReviewerUserId)
                    return new CashDifferenceApprovalReadModelResult(approval, true);
                throw new CashSessionException(
                    CashSessionErrorCodes.VarianceNotPending,
                    $"This cash difference was already {approval.Status}.",
                    409);
            }

            if (approval.CreatedByUserId == request.ReviewerUserId)
                throw new CashSessionException(
                    CashSessionErrorCodes.VarianceSelfReview,
                    "The user who closed the session cannot review its cash difference.",
                    403);

            var now = clock.GetUtcNow().UtcDateTime;
            approval.Status = wanted;
            approval.ReviewedByUserId = request.ReviewerUserId;
            approval.ReviewedAtUtc = now;
            approval.ReviewNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            approval.DecisionVersion++;
            if (request.Approve)
            {
                approval.PostingStatus = CashDifferencePostingStatuses.Pending;
                approval.NextPostingAttemptAtUtc = now;
            }
            else
            {
                approval.PostingStatus = CashDifferencePostingStatuses.NotRequired;
                approval.NextPostingAttemptAtUtc = null;
            }

            await eventWriter.AppendAsync(
                new CashSessionEvent(
                    CashSessionEventTypes.VarianceDecided,
                    approval.BusinessId,
                    "CashDifferenceApproval",
                    approval.Id.ToString(),
                    $"decision:{approval.DecisionVersion}",
                    now,
                    new
                    {
                        businessId = approval.BusinessId,
                        approvalId = approval.Id,
                        cashSessionId = approval.CashSessionId,
                        outletId = approval.OutletId,
                        decision = wanted,
                        variance = approval.VarianceAmount,
                        reviewedByUserId = request.ReviewerUserId,
                        decisionVersion = approval.DecisionVersion
                    }),
                cancellationToken);
            await differences.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;

            await auditWriter.RecordAsync(
                request.Approve ? "CashDifferenceApproved" : "CashDifferenceRejected",
                $"Cash difference {approval.Id} {wanted} for session {approval.CashSessionId}",
                approval.CashSessionId,
                new Dictionary<string, object?>
                {
                    ["approvalId"] = approval.Id,
                    ["variance"] = approval.VarianceAmount,
                    ["reviewedByUserId"] = request.ReviewerUserId,
                    ["decisionVersion"] = approval.DecisionVersion,
                    ["notes"] = approval.ReviewNotes
                },
                cancellationToken);
            return new CashDifferenceApprovalReadModelResult(approval, false);
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

/// <summary>
/// Posts the approved difference to the General Ledger exactly once. The journal is keyed by the approval, so a retry
/// after a failure or a concurrent attempt returns the same journal. A failure is recorded as <c>failed</c> with the
/// next retry time and never reopens or changes the closed session. The background worker passes
/// <paramref name="SkipIfLocked"/> so an approval another instance is posting is skipped instead of waited for.
/// </summary>
public sealed record PostCashDifferenceCommand(long ApprovalId, long ActorUserId, bool SkipIfLocked = false)
    : ICommand<CashDifferenceApprovalReadModelResult>;

public sealed record GetCashDifferenceQuery(long ApprovalId) : IQuery<CashDifferenceApproval>;

public sealed class GetCashDifferenceQueryHandler(
    ICashDifferenceStore differences,
    ICashSessionOutletAuthorizer outletAuthorizer) : IRequestHandler<GetCashDifferenceQuery, CashDifferenceApproval>
{
    public async Task<CashDifferenceApproval> Handle(GetCashDifferenceQuery request, CancellationToken cancellationToken)
    {
        var approval = await differences.FindAsync(request.ApprovalId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash difference was not found.");
        await outletAuthorizer.AuthorizeReadAsync(approval.OutletId, cancellationToken);
        return approval;
    }
}

public sealed class PostCashDifferenceCommandHandler(
    ICashDifferenceStore differences,
    ICashSessionStore sessions,
    ICashSessionVarianceJournalPoster journalPoster,
    ICashSessionEventWriter eventWriter,
    ICashSessionAuditWriter auditWriter,
    TimeProvider clock) : IRequestHandler<PostCashDifferenceCommand, CashDifferenceApprovalReadModelResult>
{
    private const int MaxErrorLength = 500;

    public async Task<CashDifferenceApprovalReadModelResult> Handle(
        PostCashDifferenceCommand request,
        CancellationToken cancellationToken)
    {
        var posted = await TryPostAsync(request, cancellationToken);
        if (posted.Result is not null)
            return posted.Result;

        // The attempt failed and was rolled back: record the failure in its own transaction.
        return await RecordFailureAsync(request, posted.Error!, cancellationToken);
    }

    private async Task<(CashDifferenceApprovalReadModelResult? Result, string? Error)> TryPostAsync(
        PostCashDifferenceCommand request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await sessions.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var approval = await differences.LockApprovalAsync(request.ApprovalId, request.SkipIfLocked, cancellationToken);
            if (approval is null && request.SkipIfLocked)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                committed = true;
                var current = await differences.FindAsync(request.ApprovalId, cancellationToken)
                    ?? throw new KeyNotFoundException("Cash difference was not found.");
                return (new CashDifferenceApprovalReadModelResult(current, true), null);
            }

            if (approval is null)
                throw new KeyNotFoundException("Cash difference was not found.");
            if (approval.Status != CashDifferenceStatuses.Approved)
                throw new CashSessionException(
                    CashSessionErrorCodes.VarianceNotPending,
                    "Only an approved cash difference can be posted.",
                    409);

            if (approval.PostingStatus == CashDifferencePostingStatuses.Posted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                committed = true;
                return (new CashDifferenceApprovalReadModelResult(approval, true), null);
            }

            var session = await sessions.FindAsync(approval.CashSessionId, cancellationToken)
                ?? throw new KeyNotFoundException("Cash session not found.");
            var now = clock.GetUtcNow().UtcDateTime;
            var journalId = await journalPoster.PostAsync(
                new CashSessionVarianceJournalRequest(
                    approval.Id,
                    session.Id,
                    approval.BusinessId,
                    session.OutletId,
                    session.TerminalId,
                    session.BusinessDate,
                    UtcTime.Normalize(session.ClosedAt ?? now),
                    approval.VarianceAmount,
                    request.ActorUserId),
                cancellationToken)
                // "Posted" without a journal would be a silent omission (FR-GL-02); treat it as a failed attempt.
                ?? throw new InvalidOperationException("No accounting journal poster is available for the cash difference.");

            approval.PostingStatus = CashDifferencePostingStatuses.Posted;
            approval.JournalEntryId = journalId;
            approval.PostedAtUtc = now;
            approval.PostingAttempts++;
            approval.LastPostingError = null;
            approval.NextPostingAttemptAtUtc = null;
            await AppendPostingEventAsync(approval, now, cancellationToken);
            await differences.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;

            await auditWriter.RecordAsync(
                "CashDifferencePosted",
                $"Cash difference {approval.Id} posted to journal {journalId}",
                approval.CashSessionId,
                new Dictionary<string, object?> { ["approvalId"] = approval.Id, ["journalEntryId"] = journalId },
                cancellationToken);
            return (new CashDifferenceApprovalReadModelResult(approval, false), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            and not KeyNotFoundException and not CashSessionException)
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            committed = true;
            differences.DiscardPendingChanges();
            return (null, Sanitize(exception));
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<CashDifferenceApprovalReadModelResult> RecordFailureAsync(
        PostCashDifferenceCommand request,
        string error,
        CancellationToken cancellationToken)
    {
        await using var transaction = await sessions.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var approval = await differences.LockApprovalAsync(request.ApprovalId, cancellationToken)
                ?? throw new KeyNotFoundException("Cash difference was not found.");
            if (approval.PostingStatus == CashDifferencePostingStatuses.Posted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                committed = true;
                return new CashDifferenceApprovalReadModelResult(approval, true);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            approval.PostingAttempts++;
            approval.PostingStatus = CashDifferencePostingStatuses.Failed;
            approval.LastPostingError = error;
            var backoffMinutes = Math.Min(60, Math.Pow(2, Math.Min(approval.PostingAttempts, 6)));
            approval.NextPostingAttemptAtUtc = now.AddMinutes(backoffMinutes);
            await AppendPostingEventAsync(approval, now, cancellationToken);
            await differences.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new CashDifferenceApprovalReadModelResult(approval, false);
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private Task AppendPostingEventAsync(CashDifferenceApproval approval, DateTime now, CancellationToken cancellationToken) =>
        eventWriter.AppendAsync(
            new CashSessionEvent(
                CashSessionEventTypes.VariancePostingChanged,
                approval.BusinessId,
                "CashDifferenceApproval",
                approval.Id.ToString(),
                $"posting:{approval.PostingStatus}:{approval.PostingAttempts}",
                now,
                new
                {
                    businessId = approval.BusinessId,
                    approvalId = approval.Id,
                    cashSessionId = approval.CashSessionId,
                    postingStatus = approval.PostingStatus,
                    attempts = approval.PostingAttempts,
                    journalEntryId = approval.JournalEntryId,
                    error = approval.LastPostingError
                }),
            CancellationToken.None);

    private static string Sanitize(Exception exception)
    {
        var message = $"{exception.GetType().Name}: {exception.Message}";
        message = message.ReplaceLineEndings(" ");
        return message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];
    }
}

/// <summary>Retries every approved difference whose posting is due (pending or failed) for one Business.</summary>
public sealed record ProcessDueCashDifferencePostingsCommand(long BusinessId, long? OutletId, long ActorUserId, int Take = 20)
    : ICommand<int>;

public sealed class ProcessDueCashDifferencePostingsCommandHandler(
    ICashDifferenceStore differences,
    ISender sender,
    TimeProvider clock) : IRequestHandler<ProcessDueCashDifferencePostingsCommand, int>
{
    public async Task<int> Handle(ProcessDueCashDifferencePostingsCommand request, CancellationToken cancellationToken)
    {
        var due = await differences.ListDueForPostingAsync(
            request.BusinessId,
            request.OutletId,
            clock.GetUtcNow().UtcDateTime,
            Math.Clamp(request.Take, 1, 100),
            cancellationToken);
        foreach (var id in due)
            await sender.Send(new PostCashDifferenceCommand(id, request.ActorUserId), cancellationToken);
        return due.Count;
    }
}
