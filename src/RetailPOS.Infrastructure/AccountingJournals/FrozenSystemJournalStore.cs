using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Accounting;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingJournals;

public sealed class FrozenSystemJournalStore(RetailPOSDbContext db, IManualJournalDraftStore journals,
    IRequiredAuditTransaction transaction, IAuditContext audit) : IFrozenSystemJournalStore
{
    public Task<long> PostAsync(FrozenSystemJournalRequest request, long actorUserId, CancellationToken cancellationToken) =>
        ExecuteAsync(request, actorUserId, "Post", cancellationToken);

    public Task<long> ReverseAsync(FrozenSystemJournalRequest request, long actorUserId, CancellationToken cancellationToken) =>
        ExecuteAsync(request, actorUserId, "Reverse", cancellationToken);

    public async Task<long?> ResolveExistingAsync(
        FrozenSystemJournalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!db.Database.IsNpgsql())
            throw new InvalidOperationException("Frozen Accounting identity requires PostgreSQL.");
        if (audit.BusinessId != request.BusinessId || !audit.IsInitialized)
            throw new InvalidOperationException(
                "Frozen Accounting recovery needs a matching initialized Business audit context.");

        if (db.Database.CurrentTransaction is null)
            return await transaction.ExecuteAsync(
                new RequiredAuditOperation(request.BusinessId, "recover", AuditModule.Accounts,
                    "Resolve frozen system journal outcome",
                    "FrozenSystemJournalOperation", request.PostingReference),
                token => ResolveExistingInTransactionAsync(request, token), cancellationToken);

        return await ResolveExistingInTransactionAsync(request, cancellationToken);
    }

    private async Task<long?> ResolveExistingInTransactionAsync(
        FrozenSystemJournalRequest request, CancellationToken cancellationToken)
    {
        if (!audit.RequiresAtomicPersistence || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Frozen Accounting recovery requires a required Audit v2 transaction.");
        await LockIdentityAsync(request, cancellationToken);
        var prior = await FindMatchingOperationAsync(request, cancellationToken);
        if (prior is null)
            return null;

        var journal = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == request.BusinessId && row.Id == prior.JournalId, cancellationToken)
            ?? throw new FrozenJournalConflictException(
                "Frozen Accounting operation does not resolve to its Business-scoped journal.");
        if (journal.Status is not (JournalEntryStatuses.Posted or JournalEntryStatuses.Reversed))
            throw new FrozenJournalConflictException(
                "Frozen Accounting operation does not resolve to a completed journal.");
        return prior.JournalId;
    }

    private async Task<long> ExecuteAsync(FrozenSystemJournalRequest request, long actorUserId,
        string expectedKind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != expectedKind) throw new ArgumentException("Frozen journal operation kind mismatch.");
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Frozen Accounting identity requires PostgreSQL.");
        if (actorUserId <= 0 || audit.BusinessId != request.BusinessId || !audit.IsInitialized
            || (audit.Source != AuditSource.SystemJob && audit.RealUserId != actorUserId))
            throw new UnauthorizedAccessException("Frozen Accounting needs a matching Business and authenticated real actor.");
        if (db.Database.CurrentTransaction is not null)
        {
            if (!audit.RequiresAtomicPersistence)
                throw new InvalidOperationException("Frozen Accounting must join a required Audit v2 transaction.");
            return await ApplyAsync(request, actorUserId, cancellationToken);
        }
        return await transaction.ExecuteAsync(new RequiredAuditOperation(request.BusinessId,
            expectedKind == "Post" ? "post" : "reverse", AuditModule.Accounts,
            "Frozen system journal operation", "FrozenSystemJournalOperation", request.PostingReference),
            token => ApplyAsync(request, actorUserId, token), cancellationToken);
    }

    private async Task<long> ApplyAsync(FrozenSystemJournalRequest request, long actorUserId, CancellationToken token)
    {
        await LockIdentityAsync(request, token);
        var prior = await FindMatchingOperationAsync(request, token);
        if (prior is not null)
        {
            var completed = await db.JournalEntries.AsNoTracking().SingleAsync(row =>
                row.BusinessId == request.BusinessId && row.Id == prior.JournalId, token);
            if (completed.Status != JournalEntryStatuses.Posted && completed.Status != JournalEntryStatuses.Reversed)
                throw new FrozenJournalConflictException("Completed posting no longer resolves a successfully posted journal.");
            return prior.JournalId;
        }

        long journalId;
        if (request.Kind == "Post")
        {
            var input = request.ReadFrozenPayload<ManualJournalDraftInput>();
            var journalType = input.JournalType!.Trim(); var referenceType = input.ReferenceType!.Trim();
            if (await db.JournalEntries.AnyAsync(row => row.BusinessId == request.BusinessId && row.IsSystemGenerated
                && row.JournalType == journalType && row.ReferenceType == referenceType
                && row.ReferenceId == request.PostingReference, token))
                throw new FrozenJournalConflictException("Reference already belongs to an unbound legacy journal; implicit adoption is forbidden.");
            journalId = await journals.CreateNewSystemDraftAsync(request.BusinessId, actorUserId,
                input, request.PostingReference, token);
            await journals.PostAsync(request.BusinessId, actorUserId, journalId, token);
        }
        else
        {
            var originalId = request.OriginalJournalId!.Value;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM journal_entries WHERE business_id = {request.BusinessId} AND id = {originalId} FOR UPDATE", token);
            var original = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == request.BusinessId && row.Id == originalId, token)
                ?? throw new KeyNotFoundException("Original scoped journal was not found.");
            if (!original.IsSystemGenerated || original.Status != JournalEntryStatuses.Posted
                || original.ReversedByJournalEntryId is not null)
                throw new FrozenJournalConflictException("Original journal is not eligible for a new system correction identity.");
            journalId = await journals.ReverseAsync(request.BusinessId, actorUserId, originalId,
                request.ReadFrozenPayload<ManualJournalReversalInput>(), token);
        }
        db.Set<FrozenSystemJournalOperation>().Add(new()
        {
            BusinessId = request.BusinessId, PostingReference = request.PostingReference, Kind = request.Kind,
            CanonicalPayloadJson = request.CanonicalPayloadJson, PayloadHash = request.PayloadHash,
            JournalId = journalId, OriginalJournalId = request.OriginalJournalId, CompletedAtUtc = DateTime.UtcNow
        });
        return journalId;
    }

    private async Task LockIdentityAsync(
        FrozenSystemJournalRequest request, CancellationToken cancellationToken)
    {
        var identity = $"accounting:frozen:{request.BusinessId}:{request.PostingReference}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0))",
            cancellationToken);
    }

    private Task<FrozenSystemJournalOperation?> FindMatchingOperationAsync(
        FrozenSystemJournalRequest request, CancellationToken cancellationToken) =>
        FindMatchingOperationCoreAsync(request, cancellationToken);

    private async Task<FrozenSystemJournalOperation?> FindMatchingOperationCoreAsync(
        FrozenSystemJournalRequest request, CancellationToken cancellationToken)
    {
        var prior = await db.Set<FrozenSystemJournalOperation>().SingleOrDefaultAsync(row =>
            row.BusinessId == request.BusinessId && row.PostingReference == request.PostingReference,
            cancellationToken);
        if (prior is not null && (prior.Kind != request.Kind
                || prior.PayloadHash != request.PayloadHash
                || prior.CanonicalPayloadJson != request.CanonicalPayloadJson
                || prior.OriginalJournalId != request.OriginalJournalId))
            throw new FrozenJournalConflictException(
                "Posting reference was reused with different frozen content.");
        return prior;
    }
}
