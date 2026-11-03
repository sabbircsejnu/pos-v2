using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.FinancialPostings;

public enum SaleFinancialPostingOutcome
{
    Posted,
    AlreadyPosted,

    /// <summary>A reversal whose sale journal has not posted yet; it is re-checked later. Not a failure.</summary>
    Deferred,

    Failed,

    /// <summary>Another worker holds the posting row; nothing was done.</summary>
    ClaimedElsewhere
}

public sealed record SaleFinancialPostingResult(SaleFinancialPostingOutcome Outcome, SaleFinancialPosting Posting);

/// <summary>
/// Posts one intent to the General Ledger exactly once (D10). Claim, journal creation/posting and the
/// <c>posted</c> transition share one transaction under the posting row lock, and the journal is keyed by the sale,
/// so a retry, a second worker or a crash can never produce a second journal. A failure rolls everything back and is
/// recorded in its own transaction with the next retry time; the committed sale is never touched.
/// </summary>
public sealed record ProcessSaleFinancialPostingCommand(long PostingId, bool SkipIfLocked = true)
    : ICommand<SaleFinancialPostingResult>;

public sealed class ProcessSaleFinancialPostingCommandHandler(
    ISaleFinancialPostingStore store,
    ISaleFinancialJournalPoster journalPoster,
    ICashSessionEventWriter eventWriter,
    IAccountingConfigurationTenantScope tenantScope,
    SaleFinancialPostingOptions options,
    TimeProvider clock) : IRequestHandler<ProcessSaleFinancialPostingCommand, SaleFinancialPostingResult>
{
    public async Task<SaleFinancialPostingResult> Handle(
        ProcessSaleFinancialPostingCommand request,
        CancellationToken cancellationToken)
    {
        var attempt = await TryPostAsync(request, cancellationToken);
        if (attempt.Result is not null)
            return attempt.Result;
        return await RecordFailureAsync(request.PostingId, attempt.Code!, attempt.Error!, cancellationToken);
    }

    private async Task<(SaleFinancialPostingResult? Result, string? Code, string? Error)> TryPostAsync(
        ProcessSaleFinancialPostingCommand request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var finished = false;
        try
        {
            var posting = await store.LockAsync(request.PostingId, request.SkipIfLocked, cancellationToken);
            if (posting is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                finished = true;
                var current = await store.FindAsync(request.PostingId, cancellationToken)
                    ?? throw new KeyNotFoundException("Financial posting was not found.");
                return (new SaleFinancialPostingResult(SaleFinancialPostingOutcome.ClaimedElsewhere, current), null, null);
            }

            // The accounts and outbox are resolved in the active (HTTP or worker) Business; it must own the posting.
            if (await tenantScope.GetBusinessIdAsync(cancellationToken) != posting.BusinessId)
                throw new UnauthorizedAccessException("The financial posting belongs to another Business.");

            if (posting.Status == SaleFinancialPostingStatuses.Posted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                finished = true;
                return (new SaleFinancialPostingResult(SaleFinancialPostingOutcome.AlreadyPosted, posting), null, null);
            }

            if (!SaleFinancialIntentFactory.IsIntact(posting))
                throw new SaleFinancialSnapshotException(
                    $"Snapshot of financial posting {posting.Id} does not match its commit-time hash.");

            var now = clock.GetUtcNow().UtcDateTime;
            long journalId;
            if (posting.Kind == SaleFinancialPostingKinds.Sale)
            {
                var posted = await journalPoster.PostSaleAsync(
                    SaleFinancialIntentFactory.ReadSaleSnapshot(posting),
                    cancellationToken);
                await store.SnapshotPaymentAccountsAsync(posting.SaleId, posted.PaymentAccounts, cancellationToken);
                journalId = posted.JournalEntryId;
            }
            else
            {
                // The reversal reuses the payment accounts the sale journal recorded, so it must not run first.
                var saleStatus = await store.FindSalePostingStatusAsync(posting.SaleId, cancellationToken);
                if (saleStatus is not null && saleStatus != SaleFinancialPostingStatuses.Posted)
                {
                    posting.LastErrorCode = SaleFinancialPostingErrorCodes.AwaitingSalePosting;
                    posting.LastError = "Waiting for the sale journal to post before the reversal.";
                    posting.NextAttemptAtUtc = now.AddSeconds(options.AwaitingSaleRetrySeconds);
                    posting.UpdatedAtUtc = now;
                    await store.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    finished = true;
                    return (new SaleFinancialPostingResult(SaleFinancialPostingOutcome.Deferred, posting), null, null);
                }

                journalId = await journalPoster.PostReversalAsync(
                    SaleFinancialIntentFactory.ReadReversalSnapshot(posting),
                    await store.GetPaymentAccountsAsync(posting.SaleId, cancellationToken),
                    cancellationToken);
            }

            posting.Status = SaleFinancialPostingStatuses.Posted;
            posting.JournalEntryId = journalId;
            posting.PostedAtUtc = now;
            posting.Attempts++;
            posting.LastAttemptAtUtc = now;
            posting.LastErrorCode = null;
            posting.LastError = null;
            posting.NextAttemptAtUtc = null;
            posting.UpdatedAtUtc = now;
            await AppendChangedEventAsync(posting, now);
            await store.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            finished = true;
            return (new SaleFinancialPostingResult(SaleFinancialPostingOutcome.Posted, posting), null, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            and not KeyNotFoundException and not UnauthorizedAccessException)
        {
            if (!finished)
                await transaction.RollbackAsync(CancellationToken.None);
            finished = true;
            store.DiscardPendingChanges();
            var (code, error) = SaleFinancialPostingFailure.Classify(exception);
            return (null, code, error);
        }
        catch
        {
            if (!finished)
                await transaction.RollbackAsync(CancellationToken.None);
            store.DiscardPendingChanges();
            throw;
        }
    }

    private async Task<SaleFinancialPostingResult> RecordFailureAsync(
        long postingId,
        string code,
        string error,
        CancellationToken cancellationToken)
    {
        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var finished = false;
        try
        {
            var posting = await store.LockAsync(postingId, skipLocked: false, cancellationToken)
                ?? throw new KeyNotFoundException("Financial posting was not found.");
            if (posting.Status == SaleFinancialPostingStatuses.Posted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                finished = true;
                return new SaleFinancialPostingResult(SaleFinancialPostingOutcome.AlreadyPosted, posting);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            posting.Attempts++;
            posting.Status = SaleFinancialPostingStatuses.Failed;
            posting.LastErrorCode = code;
            posting.LastError = error;
            posting.LastAttemptAtUtc = now;
            posting.NextAttemptAtUtc = now.Add(options.Backoff(posting.Attempts));
            posting.UpdatedAtUtc = now;
            await AppendChangedEventAsync(posting, now);
            await store.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            finished = true;
            return new SaleFinancialPostingResult(SaleFinancialPostingOutcome.Failed, posting);
        }
        catch
        {
            if (!finished)
                await transaction.RollbackAsync(CancellationToken.None);
            store.DiscardPendingChanges();
            throw;
        }
    }

    private Task AppendChangedEventAsync(SaleFinancialPosting posting, DateTime now) =>
        eventWriter.AppendAsync(
            new CashSessionEvent(
                SaleFinancialPostingEventTypes.Changed,
                posting.BusinessId,
                nameof(SaleFinancialPosting),
                posting.SourceKey,
                $"{posting.Status}:{posting.Attempts}",
                now,
                new
                {
                    businessId = posting.BusinessId,
                    postingId = posting.Id,
                    saleId = posting.SaleId,
                    kind = posting.Kind,
                    sourceKey = posting.SourceKey,
                    status = posting.Status,
                    attempts = posting.Attempts,
                    journalEntryId = posting.JournalEntryId,
                    errorCode = posting.LastErrorCode
                }),
            CancellationToken.None);
}

/// <summary>
/// Operator retry of one posting in the caller's Business, regardless of its scheduled retry time (P3.2-D03). It only
/// re-runs the existing immutable intent: there is no way to supply amounts, dates or a new snapshot, and it never
/// creates a posting. Concurrent retries serialise on the row lock and the journal key, so they are idempotent.
/// </summary>
public sealed record RetrySaleFinancialPostingCommand(long PostingId) : ICommand<SaleFinancialPostingResult>;

public sealed class RetrySaleFinancialPostingCommandHandler(
    ISaleFinancialPostingStore store,
    IAccountingConfigurationTenantScope tenantScope,
    ISaleMutationOutletAuthorizer outletAuthorizer,
    ISaleFinancialPostingAuditWriter auditWriter,
    ISender sender,
    TimeProvider clock) : IRequestHandler<RetrySaleFinancialPostingCommand, SaleFinancialPostingResult>
{
    public async Task<SaleFinancialPostingResult> Handle(
        RetrySaleFinancialPostingCommand request,
        CancellationToken cancellationToken)
    {
        var requestedAt = clock.GetUtcNow().UtcDateTime;
        var posting = await store.FindAsync(request.PostingId, cancellationToken);
        // A posting of another Business is reported as missing so its existence is not disclosed.
        if (posting is null || posting.BusinessId != await tenantScope.GetBusinessIdAsync(cancellationToken))
            throw new KeyNotFoundException("Financial posting was not found.");
        await outletAuthorizer.AuthorizeAsync(posting.OutletId, cancellationToken);
        var result = posting.Status == SaleFinancialPostingStatuses.Posted
            ? new SaleFinancialPostingResult(SaleFinancialPostingOutcome.AlreadyPosted, posting)
            : await sender.Send(new ProcessSaleFinancialPostingCommand(posting.Id, SkipIfLocked: false), cancellationToken);
        await auditWriter.RecordRetryAsync(
            new SaleFinancialPostingRetryAudit(
                posting.BusinessId,
                posting.Id,
                posting.SourceKey,
                posting.Status,
                requestedAt,
                result.Outcome.ToString(),
                result.Posting.Status),
            CancellationToken.None);
        return result;
    }
}

public sealed record GetSaleFinancialPostingsQuery(string? Status, int Take = 50) : IQuery<IReadOnlyList<SaleFinancialPosting>>;

public sealed class GetSaleFinancialPostingsQueryHandler(
    ISaleFinancialPostingStore store,
    IAccountingConfigurationTenantScope tenantScope)
    : IRequestHandler<GetSaleFinancialPostingsQuery, IReadOnlyList<SaleFinancialPosting>>
{
    public async Task<IReadOnlyList<SaleFinancialPosting>> Handle(
        GetSaleFinancialPostingsQuery request,
        CancellationToken cancellationToken)
    {
        var status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim().ToLowerInvariant();
        if (status is not (null or SaleFinancialPostingStatuses.Pending or SaleFinancialPostingStatuses.Posted
            or SaleFinancialPostingStatuses.Failed))
            throw new ArgumentException("Status must be pending, posted or failed.", nameof(request.Status));
        return await store.ListAsync(
            await tenantScope.GetBusinessIdAsync(cancellationToken),
            status,
            Math.Clamp(request.Take, 1, 200),
            cancellationToken);
    }
}

public sealed record GetSaleFinancialPostingSummaryQuery : IQuery<SaleFinancialPostingSummary>;

public sealed class GetSaleFinancialPostingSummaryQueryHandler(
    ISaleFinancialPostingStore store,
    IAccountingConfigurationTenantScope tenantScope)
    : IRequestHandler<GetSaleFinancialPostingSummaryQuery, SaleFinancialPostingSummary>
{
    public async Task<SaleFinancialPostingSummary> Handle(
        GetSaleFinancialPostingSummaryQuery request,
        CancellationToken cancellationToken) =>
        await store.SummarizeAsync(await tenantScope.GetBusinessIdAsync(cancellationToken), cancellationToken);
}
