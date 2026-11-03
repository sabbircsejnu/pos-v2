using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingJournals;

public sealed class ManualJournalDraftStore(
    RetailPOSDbContext dbContext,
    IAuditService auditService,
    IJournalNumberAllocator journalNumberAllocator,
    ILogger<ManualJournalDraftStore> logger)
    : IManualJournalDraftStore
{
    private const int AccountingScale = 2;

    public Task<long> CreateOrGetSystemDraftAsync(
        long businessId,
        long actorUserId,
        ManualJournalDraftInput input,
        string? idempotencyDiscriminator,
        CancellationToken cancellationToken) =>
        CreateSystemDraftAsync(businessId, actorUserId, input, idempotencyDiscriminator, false, cancellationToken);

    public Task<long> CreateNewSystemDraftAsync(
        long businessId,
        long actorUserId,
        ManualJournalDraftInput input,
        string? idempotencyDiscriminator,
        CancellationToken cancellationToken) =>
        CreateSystemDraftAsync(businessId, actorUserId, input, idempotencyDiscriminator, true, cancellationToken);

    private async Task<long> CreateSystemDraftAsync(
        long businessId,
        long actorUserId,
        ManualJournalDraftInput input,
        string? idempotencyDiscriminator,
        bool requireNewIdentity,
        CancellationToken cancellationToken)
    {
        try
        {
            var journalType = NormalizeJournalType(input.JournalType, isSystemGenerated: true);
            var referenceType = NormalizeManualReferenceType(
                input.ReferenceType,
                isSystemGenerated: true,
                existingValue: null);
            var referenceId = NormalizeManualReferenceId(
                input.ReferenceId,
                isSystemGenerated: true,
                existingValue: null);

            var existing = await FindExistingSystemJournalAsync(
                businessId,
                journalType,
                referenceType,
                referenceId,
                cancellationToken);
            if (existing is not null)
            {
                if (requireNewIdentity)
                    throw new FrozenJournalConflictException("System reference already belongs to an unbound journal.");
                await RecordSystemDraftAuditAsync(existing, idempotencyDiscriminator);
                return existing.Id;
            }

            await ValidateAndLoadAccountsAsync(
                businessId,
                input.Lines,
                isManual: false,
                cancellationToken);

            var now = DateTime.UtcNow;
            var journal = new JournalEntry
            {
                BusinessId = businessId,
                JournalType = journalType,
                TransactionDate = input.TransactionDate,
                PostingDate = input.PostingDate,
                BusinessDate = (input.BusinessDate ?? input.TransactionDate).Date,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                ReferenceNumber = NormalizeOptional(input.ReferenceNumber, 80),
                Description = NormalizeOptional(input.Description, 500),
                OutletId = input.OutletId,
                WarehouseId = input.WarehouseId,
                TerminalId = input.TerminalId,
                CashSessionId = input.CashSessionId,
                Status = JournalEntryStatuses.Draft,
                IsSystemGenerated = true,
                CreatedAt = now,
                CreatedBy = actorUserId,
                UpdatedAt = now,
                UpdatedBy = actorUserId,
                Lines = BuildLines(input.Lines, actorUserId, now)
            };

            dbContext.JournalEntries.Add(journal);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                if (requireNewIdentity)
                    throw;
                foreach (var line in journal.Lines.ToList())
                    dbContext.Entry(line).State = EntityState.Detached;
                dbContext.Entry(journal).State = EntityState.Detached;

                existing = await FindExistingSystemJournalAsync(
                    businessId,
                    journalType,
                    referenceType,
                    referenceId,
                    cancellationToken);
                if (existing is null)
                    throw;

                await RecordSystemDraftAuditAsync(existing, idempotencyDiscriminator);
                return existing.Id;
            }

            logger.LogInformation(
                "System journal draft {JournalId} created for business {BusinessId}",
                journal.Id,
                businessId);
            await RecordSystemDraftAuditAsync(journal, idempotencyDiscriminator);
            return journal.Id;
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                exception.Message,
                AuditActionType.Create,
                "System journal draft creation rejected",
                null);
            throw;
        }
    }

    public Task<long> PostAsync(
        long businessId,
        long actorUserId,
        long journalId,
        CancellationToken cancellationToken) =>
        PostInternalAsync(businessId, actorUserId, journalId, null, cancellationToken);

    public Task<long> PostApprovedYearEndClosingAsync(
        long businessId,
        long actorUserId,
        long journalId,
        long financialYearCloseRunId,
        CancellationToken cancellationToken) =>
        PostInternalAsync(
            businessId,
            actorUserId,
            journalId,
            financialYearCloseRunId,
            cancellationToken);

    private async Task<long> PostInternalAsync(
        long businessId,
        long actorUserId,
        long journalId,
        long? financialYearCloseRunId,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (ownsTransaction)
                transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            await LockJournalRowAsync(journalId, cancellationToken);
            var journal = await dbContext.JournalEntries
                .Include(entry => entry.Lines)
                .FirstOrDefaultAsync(
                    entry => entry.BusinessId == businessId && entry.Id == journalId,
                    cancellationToken)
                ?? throw new KeyNotFoundException($"Journal entry with ID {journalId} was not found.");
            await dbContext.Entry(journal).ReloadAsync(cancellationToken);

            if (journal.Status == JournalEntryStatuses.Posted
                || journal.Status == JournalEntryStatuses.Reversed)
                return journal.Id;

            if (journal.Status == JournalEntryStatuses.Cancelled)
                throw new InvalidOperationException("Cancelled journals cannot be posted.");

            var isManual = !journal.IsSystemGenerated;
            if (!isManual)
            {
                if (journal.Status != JournalEntryStatuses.Draft)
                    throw new InvalidOperationException("Only draft journals can be posted.");
            }
            else
            {
                var workflowMode = await dbContext.AccountingConfigurations
                    .AsNoTracking()
                    .Where(configuration => configuration.BusinessId == businessId)
                    .Select(configuration => configuration.ManualJournalWorkflowMode)
                    .FirstOrDefaultAsync(cancellationToken);
                var allowedStatus = string.Equals(
                    workflowMode,
                    ManualJournalWorkflowModes.ApprovalRequired,
                    StringComparison.OrdinalIgnoreCase)
                    ? JournalEntryStatuses.Approved
                    : JournalEntryStatuses.Draft;

                if (!string.Equals(
                        journal.Status,
                        allowedStatus,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Manual journal can only be posted from status '{allowedStatus}' under the configured workflow mode. Current status: {journal.Status}.");
            }

            var lines = journal.Lines
                .Select(line => new ManualJournalDraftLineInput(
                    line.AccountId,
                    line.Description,
                    line.DebitAmount,
                    line.CreditAmount,
                    line.OutletId,
                    line.WarehouseId,
                    line.SupplierId,
                    line.CustomerId,
                    line.PaymentMethodId,
                    line.CostCenterId))
                .ToList();
            ValidateLines(lines, isManual);
            await ValidateAndLoadAccountsAsync(businessId, lines, isManual, cancellationToken);
            if (financialYearCloseRunId.HasValue)
                await ValidateApprovedYearEndPostingPeriodAsync(
                    businessId,
                    journal,
                    financialYearCloseRunId.Value,
                    cancellationToken);
            else
                await ValidateOpenPostingPeriodAsync(businessId, journal, cancellationToken);

            var now = DateTime.UtcNow;
            journal.JournalNumber ??= await journalNumberAllocator.AllocateNextJournalNumberAsync(
                businessId,
                journal.FinancialYearId,
                journal.PostingDate,
                actorUserId,
                cancellationToken);

            var previousStatus = journal.Status;
            journal.Status = JournalEntryStatuses.Posted;
            journal.PostedAt = now;
            journal.PostedBy = actorUserId;
            journal.UpdatedAt = now;
            journal.UpdatedBy = actorUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            if (ownsTransaction && transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Journal {JournalId} posted for business {BusinessId}",
                journalId,
                businessId);
            await RecordStateChangedAuditAsync(
                journal,
                $"Journal posted: {journal.Id}",
                [
                    ("Status", previousStatus, journal.Status),
                    ("JournalNumber", null, journal.JournalNumber),
                    ("PostedBy", null, journal.PostedBy),
                    ("PostedAt", null, journal.PostedAt)
                ],
                AuditActionType.Approve);
            return journal.Id;
        }
        catch (Exception exception)
        {
            if (ownsTransaction && transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    logger.LogWarning(
                        rollbackException,
                        "Failed to roll back posting of journal {JournalId}",
                        journalId);
                }
            }

            await RecordFailureAuditAsync(
                exception.Message,
                AuditActionType.Reject,
                $"Journal posting rejected: {journalId}",
                journalId.ToString());
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<long> ReverseAsync(
        long businessId,
        long actorUserId,
        long journalId,
        ManualJournalReversalInput input,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (string.IsNullOrWhiteSpace(input.Reason))
                throw new InvalidOperationException("Reversal reason is required.");

            if (ownsTransaction)
                transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            await LockJournalRowAsync(journalId, cancellationToken);
            var original = await dbContext.JournalEntries
                .Include(entry => entry.Lines)
                .FirstOrDefaultAsync(
                    entry => entry.BusinessId == businessId && entry.Id == journalId,
                    cancellationToken)
                ?? throw new KeyNotFoundException($"Journal entry with ID {journalId} was not found.");

            if (original.Status != JournalEntryStatuses.Posted)
                throw new InvalidOperationException("Only posted journals can be reversed.");

            if (string.Equals(
                    original.JournalType,
                    AccountingJournalTypes.OpeningBalance,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    original.JournalType,
                    AccountingJournalTypes.OpeningBalanceAdjustment,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Opening balance journals cannot be reversed. Use Opening Balance Adjustment journals for corrections.");

            if (original.ReversedByJournalEntryId.HasValue)
                throw new InvalidOperationException("Journal is already reversed.");

            var existingReversal = await dbContext.JournalEntries
                .Include(entry => entry.Lines)
                .FirstOrDefaultAsync(
                    entry => entry.BusinessId == businessId
                        && entry.ReversalOfJournalEntryId == journalId,
                    cancellationToken);
            if (existingReversal is not null)
            {
                original.ReversedByJournalEntryId ??= existingReversal.Id;
                original.Status = JournalEntryStatuses.Reversed;
                original.ReversedAt ??= DateTime.UtcNow;
                original.ReversedBy ??= actorUserId;
                await dbContext.SaveChangesAsync(cancellationToken);
                if (ownsTransaction && transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return existingReversal.Id;
            }

            var reason = input.Reason.Trim();
            var now = DateTime.UtcNow;
            var reversalPostingDate = input.PostingDate ?? now;
            var reversalBusinessDate = (input.BusinessDate ?? reversalPostingDate).Date;
            var reversal = new JournalEntry
            {
                BusinessId = businessId,
                JournalType = AccountingJournalTypes.JournalReversal,
                TransactionDate = now,
                PostingDate = reversalPostingDate,
                BusinessDate = reversalBusinessDate,
                ReferenceType = original.ReferenceType,
                ReferenceId = original.ReferenceId,
                ReferenceNumber = original.ReferenceNumber,
                Description = $"Reversal of {original.JournalNumber}: {reason}",
                OutletId = original.OutletId,
                WarehouseId = original.WarehouseId,
                TerminalId = original.TerminalId,
                CashSessionId = original.CashSessionId,
                Status = JournalEntryStatuses.Draft,
                IsSystemGenerated = original.IsSystemGenerated,
                ReversalOfJournalEntryId = original.Id,
                CreatedAt = now,
                CreatedBy = actorUserId,
                UpdatedAt = now,
                UpdatedBy = actorUserId,
                Lines = original.Lines.Select(line => new JournalEntryLine
                {
                    AccountId = line.AccountId,
                    Description = line.Description,
                    DebitAmount = line.CreditAmount,
                    CreditAmount = line.DebitAmount,
                    OutletId = line.OutletId,
                    WarehouseId = line.WarehouseId,
                    SupplierId = line.SupplierId,
                    CustomerId = line.CustomerId,
                    PaymentMethodId = line.PaymentMethodId,
                    CostCenterId = line.CostCenterId,
                    CreatedAt = now,
                    CreatedBy = actorUserId
                }).ToList()
            };

            var reversalLines = reversal.Lines
                .Select(line => new ManualJournalDraftLineInput(
                    line.AccountId,
                    line.Description,
                    line.DebitAmount,
                    line.CreditAmount,
                    line.OutletId,
                    line.WarehouseId,
                    line.SupplierId,
                    line.CustomerId,
                    line.PaymentMethodId,
                    line.CostCenterId))
                .ToList();
            ValidateLines(reversalLines, isManual: !reversal.IsSystemGenerated);
            await ValidateAndLoadAccountsAsync(
                businessId,
                reversalLines,
                isManual: !reversal.IsSystemGenerated,
                cancellationToken);
            await ValidateOpenPostingPeriodAsync(businessId, reversal, cancellationToken);
            reversal.JournalNumber = await journalNumberAllocator.AllocateNextJournalNumberAsync(
                businessId,
                reversal.FinancialYearId,
                reversal.PostingDate,
                actorUserId,
                cancellationToken);
            reversal.Status = JournalEntryStatuses.Posted;
            reversal.PostedAt = now;
            reversal.PostedBy = actorUserId;

            dbContext.JournalEntries.Add(reversal);
            await dbContext.SaveChangesAsync(cancellationToken);

            original.Status = JournalEntryStatuses.Reversed;
            original.ReversedByJournalEntryId = reversal.Id;
            original.ReversedAt = now;
            original.ReversedBy = actorUserId;
            original.UpdatedAt = now;
            original.UpdatedBy = actorUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
            if (ownsTransaction && transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Journal {JournalId} reversed by {ReversalId}",
                original.Id,
                reversal.Id);
            await RecordStateChangedAuditAsync(
                reversal,
                $"Journal reversed: {original.Id}",
                [
                    ("Status", JournalEntryStatuses.Posted, JournalEntryStatuses.Reversed),
                    ("ReversalOfJournalEntryId", null, reversal.ReversalOfJournalEntryId),
                    ("ReversalJournalEntryId", null, reversal.Id),
                    ("Reason", null, reason)
                ],
                AuditActionType.Reverse,
                new Dictionary<string, object?>
                {
                    ["reason"] = reason,
                    ["originalJournalId"] = original.Id,
                    ["reversalJournalId"] = reversal.Id
                });
            return reversal.Id;
        }
        catch (Exception exception)
        {
            if (ownsTransaction && transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    logger.LogWarning(
                        rollbackException,
                        "Failed to roll back reversal of journal {JournalId}",
                        journalId);
                }
            }

            await RecordFailureAuditAsync(
                exception.Message,
                AuditActionType.Reject,
                $"Journal reversal rejected: {journalId}",
                journalId.ToString());
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<long> CreateAsync(
        long businessId,
        long actorUserId,
        ManualJournalDraftInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var lines = input.Lines;
            ValidateLines(lines, isManual: true);
            await ValidateAndLoadAccountsAsync(businessId, lines, isManual: true, cancellationToken);

            var journalType = NormalizeJournalType(input.JournalType);
            var referenceType = NormalizeOptional(input.ReferenceType, 50) ?? "ManualJournal";
            var referenceId = NormalizeOptional(input.ReferenceId, 80)
                ?? Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            var journal = new JournalEntry
            {
                BusinessId = businessId,
                JournalType = journalType,
                TransactionDate = input.TransactionDate,
                PostingDate = input.PostingDate,
                BusinessDate = (input.BusinessDate ?? input.TransactionDate).Date,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                ReferenceNumber = NormalizeOptional(input.ReferenceNumber, 80),
                Description = NormalizeRequired(input.Description, nameof(input.Description), 500),
                OutletId = input.OutletId,
                WarehouseId = input.WarehouseId,
                TerminalId = input.TerminalId,
                CashSessionId = input.CashSessionId,
                Status = JournalEntryStatuses.Draft,
                IsSystemGenerated = false,
                CreatedAt = now,
                CreatedBy = actorUserId,
                UpdatedAt = now,
                UpdatedBy = actorUserId,
                Lines = BuildLines(lines, actorUserId, now)
            };

            dbContext.JournalEntries.Add(journal);
            await dbContext.SaveChangesAsync(cancellationToken);
            await RecordCreatedAuditAsync(journal);
            logger.LogInformation(
                "Manual journal draft {JournalId} created for business {BusinessId}",
                journal.Id,
                businessId);
            return journal.Id;
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(exception.Message);
            throw;
        }
    }

    public async Task<long> UpdateAsync(
        long businessId,
        long actorUserId,
        long journalId,
        ManualJournalDraftUpdateInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var journal = await dbContext.JournalEntries
                .Include(entry => entry.Lines)
                .FirstOrDefaultAsync(
                    entry => entry.BusinessId == businessId && entry.Id == journalId,
                    cancellationToken)
                ?? throw new KeyNotFoundException($"Journal entry with ID {journalId} was not found.");

            if (!string.Equals(journal.Status, JournalEntryStatuses.Draft, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(journal.Status, JournalEntryStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only Draft or Rejected manual journals can be updated.");

            var lines = input.Lines;
            var isManual = !journal.IsSystemGenerated;
            ValidateLines(lines, isManual);
            await ValidateAndLoadAccountsAsync(businessId, lines, isManual, cancellationToken);

            var previousJournalType = journal.JournalType;
            var previousTransactionDate = journal.TransactionDate;
            var previousPostingDate = journal.PostingDate;
            var previousBusinessDate = journal.BusinessDate;
            var previousReferenceType = journal.ReferenceType;
            var previousReferenceId = journal.ReferenceId;
            var previousReferenceNumber = journal.ReferenceNumber;
            var previousDescription = journal.Description;
            var previousOutletId = journal.OutletId;
            var previousWarehouseId = journal.WarehouseId;
            var previousLineCount = journal.Lines.Count;
            var previousDebitTotal = journal.Lines.Sum(line => line.DebitAmount);
            var previousCreditTotal = journal.Lines.Sum(line => line.CreditAmount);

            journal.JournalType = NormalizeJournalType(input.JournalType, journal.IsSystemGenerated);
            journal.TransactionDate = input.TransactionDate;
            journal.PostingDate = input.PostingDate;
            journal.BusinessDate = (input.BusinessDate ?? input.TransactionDate).Date;
            journal.ReferenceType = NormalizeManualReferenceType(
                input.ReferenceType,
                journal.IsSystemGenerated,
                journal.ReferenceType);
            journal.ReferenceId = NormalizeManualReferenceId(
                input.ReferenceId,
                journal.IsSystemGenerated,
                journal.ReferenceId);
            journal.ReferenceNumber = NormalizeOptional(input.ReferenceNumber, 80);
            journal.Description = journal.IsSystemGenerated
                ? NormalizeOptional(input.Description, 500)
                : NormalizeRequired(input.Description, nameof(input.Description), 500);
            journal.OutletId = input.OutletId;
            journal.WarehouseId = input.WarehouseId;
            journal.TerminalId = input.TerminalId;
            journal.CashSessionId = input.CashSessionId;
            journal.UpdatedAt = DateTime.UtcNow;
            journal.UpdatedBy = actorUserId;
            journal.RejectedAt = null;
            journal.RejectedBy = null;
            journal.RejectionReason = null;

            dbContext.JournalEntryLines.RemoveRange(journal.Lines);
            journal.Lines = BuildLines(lines, actorUserId, DateTime.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);

            await RecordUpdatedAuditAsync(
                journal,
                [
                    ("JournalType", previousJournalType, journal.JournalType),
                    ("TransactionDate", previousTransactionDate, journal.TransactionDate),
                    ("PostingDate", previousPostingDate, journal.PostingDate),
                    ("BusinessDate", previousBusinessDate, journal.BusinessDate),
                    ("ReferenceType", previousReferenceType, journal.ReferenceType),
                    ("ReferenceId", previousReferenceId, journal.ReferenceId),
                    ("ReferenceNumber", previousReferenceNumber, journal.ReferenceNumber),
                    ("Description", previousDescription, journal.Description),
                    ("OutletId", previousOutletId, journal.OutletId),
                    ("WarehouseId", previousWarehouseId, journal.WarehouseId),
                    ("LineCount", previousLineCount, journal.Lines.Count),
                    ("DebitTotal", previousDebitTotal, journal.Lines.Sum(line => line.DebitAmount)),
                    ("CreditTotal", previousCreditTotal, journal.Lines.Sum(line => line.CreditAmount))
                ]);
            return journal.Id;
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                exception.Message,
                AuditActionType.Update,
                $"Journal draft update rejected: {journalId}",
                journalId.ToString());
            throw;
        }
    }

    public async Task<long> SubmitAsync(
        long businessId,
        long actorUserId,
        long journalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await dbContext.AccountingConfigurations
                .FirstOrDefaultAsync(
                    item => item.BusinessId == businessId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Accounting configuration is required for manual journal approval workflow.");
            if (!string.Equals(
                    configuration.ManualJournalWorkflowMode,
                    ManualJournalWorkflowModes.ApprovalRequired,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Manual journal approval workflow is not enabled for this business.");

            var journal = await dbContext.JournalEntries
                .FirstOrDefaultAsync(
                    entry => entry.BusinessId == businessId && entry.Id == journalId,
                    cancellationToken)
                ?? throw new KeyNotFoundException($"Journal entry with ID {journalId} was not found.");

            EnsureManualJournal(journal);
            if (!string.Equals(journal.Status, JournalEntryStatuses.Draft, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(journal.Status, JournalEntryStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Only Draft or Rejected manual journals can be submitted. Current status: {journal.Status}.");

            var previousStatus = journal.Status;
            journal.Status = JournalEntryStatuses.Submitted;
            journal.SubmittedAt = DateTime.UtcNow;
            journal.SubmittedBy = actorUserId;
            journal.UpdatedAt = journal.SubmittedAt.Value;
            journal.UpdatedBy = actorUserId;
            journal.RejectedAt = null;
            journal.RejectedBy = null;
            journal.RejectionReason = null;

            await dbContext.SaveChangesAsync(cancellationToken);
            await RecordStateChangedAuditAsync(
                journal,
                $"Journal submitted: {journal.Id}",
                [
                    ("Status", previousStatus, journal.Status),
                    ("SubmittedBy", null, journal.SubmittedBy),
                    ("SubmittedAt", null, journal.SubmittedAt)
                ]);
            return journal.Id;
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                exception.Message,
                AuditActionType.Update,
                $"Journal submission rejected: {journalId}",
                journalId.ToString());
            throw;
        }
    }

    public async Task<long> ApproveAsync(
        long businessId,
        long actorUserId,
        long journalId,
        bool isBusinessOwnerOrSuperAdmin,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await dbContext.AccountingConfigurations
                .FirstOrDefaultAsync(
                    item => item.BusinessId == businessId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Accounting configuration is required for manual journal approval workflow.");
            if (!string.Equals(
                    configuration.ManualJournalWorkflowMode,
                    ManualJournalWorkflowModes.ApprovalRequired,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Manual journal approval workflow is not enabled for this business.");

            var journal = await dbContext.JournalEntries
                .FirstOrDefaultAsync(
                    entry => entry.BusinessId == businessId && entry.Id == journalId,
                    cancellationToken)
                ?? throw new KeyNotFoundException($"Journal entry with ID {journalId} was not found.");

            EnsureManualJournal(journal);
            if (!string.Equals(journal.Status, JournalEntryStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Only Submitted manual journals can be approved. Current status: {journal.Status}.");

            var sameUser = journal.CreatedBy.HasValue && journal.CreatedBy.Value == actorUserId;
            if (sameUser
                && !(configuration.ManualJournalAllowSelfApproval && isBusinessOwnerOrSuperAdmin))
                throw new InvalidOperationException(
                    "The journal creator cannot approve their own manual journal unless self-approval is explicitly enabled for Business Owner or Super Admin.");

            var previousStatus = journal.Status;
            journal.Status = JournalEntryStatuses.Approved;
            journal.ApprovedAt = DateTime.UtcNow;
            journal.ApprovedBy = actorUserId;
            journal.UpdatedAt = journal.ApprovedAt.Value;
            journal.UpdatedBy = actorUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            await RecordStateChangedAuditAsync(
                journal,
                $"Journal approved: {journal.Id}",
                [
                    ("Status", previousStatus, journal.Status),
                    ("ApprovedBy", null, journal.ApprovedBy),
                    ("ApprovedAt", null, journal.ApprovedAt)
                ],
                AuditActionType.Approve);
            return journal.Id;
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                exception.Message,
                AuditActionType.Approve,
                $"Journal approval rejected: {journalId}",
                journalId.ToString());
            throw;
        }
    }

    public async Task<long> RejectAsync(
        long businessId,
        long actorUserId,
        long journalId,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await dbContext.AccountingConfigurations
                .FirstOrDefaultAsync(
                    item => item.BusinessId == businessId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Accounting configuration is required for manual journal approval workflow.");
            if (!string.Equals(
                    configuration.ManualJournalWorkflowMode,
                    ManualJournalWorkflowModes.ApprovalRequired,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Manual journal approval workflow is not enabled for this business.");

            var journal = await dbContext.JournalEntries
                .FirstOrDefaultAsync(
                    entry => entry.BusinessId == businessId && entry.Id == journalId,
                    cancellationToken)
                ?? throw new KeyNotFoundException($"Journal entry with ID {journalId} was not found.");

            EnsureManualJournal(journal);
            if (!string.Equals(journal.Status, JournalEntryStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Only Submitted manual journals can be rejected. Current status: {journal.Status}.");

            var normalizedReason = NormalizeRequired(reason, nameof(reason), 500);
            var previousStatus = journal.Status;
            journal.Status = JournalEntryStatuses.Rejected;
            journal.RejectedAt = DateTime.UtcNow;
            journal.RejectedBy = actorUserId;
            journal.RejectionReason = normalizedReason;
            journal.UpdatedAt = journal.RejectedAt.Value;
            journal.UpdatedBy = actorUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            await RecordStateChangedAuditAsync(
                journal,
                $"Journal rejected: {journal.Id}",
                [
                    ("Status", previousStatus, journal.Status),
                    ("RejectedBy", null, journal.RejectedBy),
                    ("RejectedAt", null, journal.RejectedAt),
                    ("RejectionReason", null, normalizedReason)
                ],
                AuditActionType.Reject,
                new Dictionary<string, object?> { ["reason"] = normalizedReason });
            return journal.Id;
        }
        catch (Exception exception)
        {
            await RecordFailureAuditAsync(
                exception.Message,
                AuditActionType.Reject,
                $"Journal rejection rejected: {journalId}",
                journalId.ToString());
            throw;
        }
    }

    public async Task<long> CancelAsync(
        long businessId,
        long actorUserId,
        long journalId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (ownsTransaction)
                transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            await LockJournalRowAsync(journalId, cancellationToken);
            var journal = await dbContext.JournalEntries
                .Include(entry => entry.Lines)
                .FirstOrDefaultAsync(
                    entry => entry.BusinessId == businessId && entry.Id == journalId,
                    cancellationToken)
                ?? throw new KeyNotFoundException($"Journal entry with ID {journalId} was not found.");

            if (!(string.Equals(journal.Status, JournalEntryStatuses.Draft, StringComparison.OrdinalIgnoreCase)
                || string.Equals(journal.Status, JournalEntryStatuses.Rejected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(journal.Status, JournalEntryStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(journal.Status, JournalEntryStatuses.Approved, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Only pre-post manual journals can be cancelled.");

            var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            var previousStatus = journal.Status;
            journal.Status = JournalEntryStatuses.Cancelled;
            journal.CancelledAt = DateTime.UtcNow;
            journal.CancelledBy = actorUserId;
            journal.CancellationReason = normalizedReason;
            journal.UpdatedAt = DateTime.UtcNow;
            journal.UpdatedBy = actorUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            if (ownsTransaction && transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            await RecordStateChangedAuditAsync(
                journal,
                $"Journal draft cancelled: {journal.Id}",
                [
                    ("Status", previousStatus, journal.Status),
                    ("CancelledBy", null, journal.CancelledBy),
                    ("CancelledAt", null, journal.CancelledAt),
                    ("CancellationReason", null, journal.CancellationReason)
                ],
                AuditActionType.Cancel,
                new Dictionary<string, object?> { ["reason"] = normalizedReason });
            return journal.Id;
        }
        catch (Exception exception)
        {
            if (ownsTransaction && transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    logger.LogWarning(
                        rollbackException,
                        "Failed to roll back cancellation of journal {JournalId}",
                        journalId);
                }
            }

            await RecordFailureAuditAsync(
                exception.Message,
                AuditActionType.Cancel,
                $"Journal draft cancellation rejected: {journalId}",
                journalId.ToString());
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task ValidateAndLoadAccountsAsync(
        long businessId,
        IReadOnlyCollection<ManualJournalDraftLineInput> lines,
        bool isManual,
        CancellationToken cancellationToken)
    {
        var accountIds = lines.Select(line => line.AccountId).Distinct().ToList();
        var accounts = await dbContext.ChartAccounts
            .Where(account => account.BusinessId == businessId && accountIds.Contains(account.Id))
            .ToListAsync(cancellationToken);

        if (accounts.Count != accountIds.Count)
            throw new InvalidOperationException("One or more accounts were not found in this business.");

        var inactiveAccounts = accounts
            .Where(account => !account.IsActive)
            .Select(account => account.AccountCode)
            .ToList();
        if (inactiveAccounts.Count > 0)
            throw new InvalidOperationException(
                $"Inactive accounts cannot be used for journal posting: {string.Join(", ", inactiveAccounts)}.");

        if (!isManual)
            return;

        var blockedAccounts = accounts
            .Where(account => !account.AllowManualPosting)
            .Select(account => account.AccountCode)
            .ToList();
        if (blockedAccounts.Count > 0)
            throw new InvalidOperationException(
                $"One or more accounts do not allow manual posting: {string.Join(", ", blockedAccounts)}.");
    }

    private async Task ValidateOpenPostingPeriodAsync(
        long businessId,
        JournalEntry journal,
        CancellationToken cancellationToken)
    {
        var postingDate = UtcCalendarDate(journal.PostingDate);
        var postingContext = await ResolveOpenPeriodContextAsync(
            businessId,
            postingDate,
            "Posting date",
            cancellationToken);
        journal.FinancialYearId = postingContext.Year.Id;
        journal.AccountingPeriodId = postingContext.Period.Id;

        if (journal.BusinessDate.HasValue
            && UtcCalendarDate(journal.BusinessDate.Value) != postingDate)
            await ResolveOpenPeriodContextAsync(
                businessId,
                UtcCalendarDate(journal.BusinessDate.Value),
                "Business date",
                cancellationToken);
    }

    private async Task ValidateApprovedYearEndPostingPeriodAsync(
        long businessId,
        JournalEntry journal,
        long financialYearCloseRunId,
        CancellationToken cancellationToken)
    {
        var closeRun = await dbContext.AccountingFinancialYearCloseRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.Id == financialYearCloseRunId
                    && entry.BusinessId == businessId
                    && entry.Status == AccountingFinancialYearCloseRunStatuses.Approved,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Approved financial year close run is required for locked-period year-end posting.");

        if (!journal.IsSystemGenerated
            || journal.JournalType != AccountingJournalTypes.YearEndClosing
            || journal.ReferenceType != AccountingReferenceTypes.FinancialYearClose
            || journal.ReferenceId != closeRun.FinancialYearId.ToString())
            throw new InvalidOperationException(
                "Locked-period posting is restricted to the approved YearEndClosing journal for its financial year close run.");

        var year = await dbContext.FinancialYears
            .SingleOrDefaultAsync(
                entry => entry.Id == closeRun.FinancialYearId
                    && entry.BusinessId == businessId
                    && !entry.IsClosed,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Financial year is not open for the approved closing run.");

        var period = await dbContext.AccountingPeriods
            .SingleOrDefaultAsync(
                entry => entry.Id == closeRun.FinalAccountingPeriodId
                    && entry.FinancialYearId == year.Id,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Final accounting period is not valid for the financial year close run.");

        var postingDate = journal.PostingDate.Date;
        if (postingDate != year.EndDate.Date
            || (journal.BusinessDate.HasValue
                && journal.BusinessDate.Value.Date != year.EndDate.Date)
            || period.Status != AccountingPeriodStatuses.Locked
            || postingDate < period.StartDate.Date
            || postingDate > period.EndDate.Date)
            throw new InvalidOperationException(
                "YearEndClosing must post on the financial year end date in the approved Locked final period.");

        journal.FinancialYearId = year.Id;
        journal.AccountingPeriodId = period.Id;
    }

    private async Task<(FinancialYear Year, AccountingPeriod Period)> ResolveOpenPeriodContextAsync(
        long businessId,
        DateTime date,
        string dateLabel,
        CancellationToken cancellationToken)
    {
        var year = await dbContext.FinancialYears
            .FirstOrDefaultAsync(
                entry => entry.BusinessId == businessId
                    && entry.StartDate.Date <= date
                    && entry.EndDate.Date >= date,
                cancellationToken);
        if (year is null)
            throw new InvalidOperationException(
                $"{dateLabel} {date:yyyy-MM-dd} does not match any financial year for this business.");
        if (year.IsClosed)
            throw new InvalidOperationException(
                $"{dateLabel} {date:yyyy-MM-dd} belongs to closed financial year '{year.Name}'.");

        var period = await dbContext.AccountingPeriods
            .FirstOrDefaultAsync(
                entry => entry.FinancialYearId == year.Id
                    && entry.StartDate.Date <= date
                    && entry.EndDate.Date >= date,
                cancellationToken);
        if (period is null)
            throw new InvalidOperationException(
                $"{dateLabel} {date:yyyy-MM-dd} does not match any accounting period in financial year '{year.Name}'.");
        if (!string.Equals(
                period.Status,
                AccountingPeriodStatuses.Open,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{dateLabel} {date:yyyy-MM-dd} belongs to accounting period '{period.Name}' with status '{period.Status}'. Only Open periods allow posting.");

        return (year, period);
    }

    // Legacy Npgsql reads timestamptz as Local; truncating first shifts UTC month boundaries.
    private static DateTime UtcCalendarDate(DateTime value) =>
        (value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc)).Date;

    private static List<JournalEntryLine> BuildLines(
        IEnumerable<ManualJournalDraftLineInput> lines,
        long actorUserId,
        DateTime now) =>
        lines.Select(line => new JournalEntryLine
        {
            AccountId = line.AccountId,
            Description = NormalizeOptional(line.Description, 300),
            DebitAmount = line.DebitAmount,
            CreditAmount = line.CreditAmount,
            OutletId = line.OutletId,
            WarehouseId = line.WarehouseId,
            SupplierId = line.SupplierId,
            CustomerId = line.CustomerId,
            PaymentMethodId = NormalizeOptional(line.PaymentMethodId, 50),
            CostCenterId = NormalizeOptional(line.CostCenterId, 50),
            CreatedAt = now,
            CreatedBy = actorUserId
        }).ToList();

    private Task<JournalEntry?> FindExistingSystemJournalAsync(
        long businessId,
        string journalType,
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken) =>
        dbContext.JournalEntries
            .Include(entry => entry.Lines)
            .FirstOrDefaultAsync(
                entry => entry.BusinessId == businessId
                    && entry.IsSystemGenerated
                    && entry.JournalType == journalType
                    && entry.ReferenceType == referenceType
                    && entry.ReferenceId == referenceId
                    && (entry.Status == JournalEntryStatuses.Draft
                        || entry.Status == JournalEntryStatuses.Posted
                        || (entry.Status == JournalEntryStatuses.Reversed
                            && entry.ReferenceType != AccountingReferenceTypes.FinancialYearClose)),
                cancellationToken);

    private static void ValidateLines(IReadOnlyCollection<ManualJournalDraftLineInput> lines, bool isManual)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("A journal requires at least one line.");
        if (lines.Count < 2)
            throw new InvalidOperationException("A journal requires at least two lines.");
        if (isManual && lines.Count > 200)
            throw new InvalidOperationException("Manual journals cannot exceed 200 lines.");

        decimal totalDebit = 0;
        decimal totalCredit = 0;
        var row = 0;
        foreach (var line in lines)
        {
            row++;
            if (line.DebitAmount < 0 || line.CreditAmount < 0)
                throw new InvalidOperationException(
                    $"Line {row}: negative debit or credit values are not allowed.");
            if (!HasAllowedScale(line.DebitAmount) || !HasAllowedScale(line.CreditAmount))
                throw new InvalidOperationException(
                    $"Line {row}: values must use a maximum of {AccountingScale} decimal places.");

            var hasDebit = line.DebitAmount > 0;
            var hasCredit = line.CreditAmount > 0;
            if (hasDebit == hasCredit)
                throw new InvalidOperationException(
                    $"Line {row}: each journal line must contain either a debit or credit amount.");

            totalDebit += line.DebitAmount;
            totalCredit += line.CreditAmount;
        }

        if (totalDebit <= 0 || totalCredit <= 0)
            throw new InvalidOperationException("Journal totals must be greater than zero.");
        if (totalDebit != totalCredit)
            throw new InvalidOperationException(
                "Journal is not balanced. Total debit must equal total credit.");
    }

    private async Task RecordCreatedAuditAsync(JournalEntry journal)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["journalId"] = journal.Id,
            ["journalNumber"] = journal.JournalNumber,
            ["businessId"] = journal.BusinessId,
            ["referenceType"] = journal.ReferenceType,
            ["referenceId"] = journal.ReferenceId,
            ["referenceNumber"] = journal.ReferenceNumber,
            ["status"] = journal.Status,
            ["submittedBy"] = journal.SubmittedBy,
            ["approvedBy"] = journal.ApprovedBy,
            ["rejectedBy"] = journal.RejectedBy,
            ["cancelledBy"] = journal.CancelledBy,
            ["postedBy"] = journal.PostedBy,
            ["reversedBy"] = journal.ReversedBy,
            ["createdBy"] = journal.CreatedBy,
            ["updatedBy"] = journal.UpdatedBy,
            ["reversalOfJournalEntryId"] = journal.ReversalOfJournalEntryId,
            ["reversedByJournalEntryId"] = journal.ReversedByJournalEntryId
        };

        try
        {
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Create,
                Module = AuditModule.Accounts,
                Summary = $"Journal draft created: {journal.Id}",
                PrimaryEntity = ("JournalEntry", journal.Id.ToString()),
                Metadata = metadata,
                Entities =
                [
                    new AuditEntityInput(
                        "JournalEntry",
                        journal.Id.ToString(),
                        AuditOperationType.Insert,
                        [("Status", null, journal.Status)])
                ]
            });
        }

        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to record journal audit event for journal {JournalId}",
                journal.Id);
        }
    }

    private Task RecordSystemDraftAuditAsync(
        JournalEntry journal,
        string? idempotencyDiscriminator) =>
        RecordStateChangedAuditAsync(
            journal,
            $"System journal draft created or resolved: {journal.Id}",
            [("Status", null, journal.Status)],
            AuditActionType.Create,
            new Dictionary<string, object?>
            {
                ["idempotencyDiscriminator"] = idempotencyDiscriminator
            });

    private async Task RecordUpdatedAuditAsync(
        JournalEntry journal,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)> fields)
    {
        await RecordStateChangedAuditAsync(
            journal,
            $"Journal draft updated: {journal.Id}",
            fields,
            AuditActionType.Update);
    }

    private async Task RecordStateChangedAuditAsync(
        JournalEntry journal,
        string summary,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)> fields,
        string actionType = AuditActionType.Update,
        IReadOnlyDictionary<string, object?>? additionalMetadata = null)
    {
        try
        {
            var metadata = new Dictionary<string, object?>(BuildAuditMetadata(journal));
            if (additionalMetadata is not null)
            {
                foreach (var entry in additionalMetadata)
                    metadata[entry.Key] = entry.Value;
            }

            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = actionType,
                Module = AuditModule.Accounts,
                Summary = summary,
                PrimaryEntity = ("JournalEntry", journal.Id.ToString()),
                Metadata = metadata,
                Entities =
                [
                    new AuditEntityInput(
                        "JournalEntry",
                        journal.Id.ToString(),
                        AuditOperationType.Update,
                        fields)
                ]
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to record journal audit event for journal {JournalId}",
                journal.Id);
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildAuditMetadata(JournalEntry journal) =>
        new Dictionary<string, object?>
        {
            ["journalId"] = journal.Id,
            ["journalNumber"] = journal.JournalNumber,
            ["businessId"] = journal.BusinessId,
            ["referenceType"] = journal.ReferenceType,
            ["referenceId"] = journal.ReferenceId,
            ["referenceNumber"] = journal.ReferenceNumber,
            ["status"] = journal.Status,
            ["submittedBy"] = journal.SubmittedBy,
            ["approvedBy"] = journal.ApprovedBy,
            ["rejectedBy"] = journal.RejectedBy,
            ["cancelledBy"] = journal.CancelledBy,
            ["postedBy"] = journal.PostedBy,
            ["reversedBy"] = journal.ReversedBy,
            ["createdBy"] = journal.CreatedBy,
            ["updatedBy"] = journal.UpdatedBy,
            ["reversalOfJournalEntryId"] = journal.ReversalOfJournalEntryId,
            ["reversedByJournalEntryId"] = journal.ReversedByJournalEntryId
        };

    private async Task RecordFailureAuditAsync(
        string errorMessage,
        string actionType = AuditActionType.Create,
        string summary = "Journal draft creation rejected",
        string? entityId = null)
    {
        try
        {
            await auditService.RecordFailureAsync(
                actionType,
                AuditModule.Accounts,
                summary,
                "JournalEntry",
                entityId,
                errorMessage);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to record rejected journal audit event");
        }
    }

    private static string NormalizeJournalType(string? journalType, bool isSystemGenerated = false)
    {
        var value = string.IsNullOrWhiteSpace(journalType)
            ? (isSystemGenerated ? AccountingJournalTypes.SystemJournal : AccountingJournalTypes.ManualJournal)
            : journalType.Trim();
        if (!isSystemGenerated && !string.Equals(
                value,
                AccountingJournalTypes.ManualJournal,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Manual journal creation only supports journal type '{AccountingJournalTypes.ManualJournal}'.");
        return value;
    }

    private static void EnsureManualJournal(JournalEntry journal)
    {
        if (!string.Equals(
                journal.JournalType,
                AccountingJournalTypes.ManualJournal,
                StringComparison.OrdinalIgnoreCase)
            || journal.IsSystemGenerated)
            throw new InvalidOperationException("This action is only supported for manual journals.");
    }

    private async Task LockJournalRowAsync(long journalId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()
            || string.Equals(
                dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
            return;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM journal_entries WHERE id = {journalId} FOR UPDATE",
            cancellationToken);
    }

    private static string NormalizeManualReferenceType(
        string? value,
        bool isSystemGenerated,
        string? existingValue)
    {
        if (isSystemGenerated)
            return NormalizeRequired(value, nameof(value), 50);
        return NormalizeOptional(value, 50)
            ?? (string.IsNullOrWhiteSpace(existingValue) ? "ManualJournal" : existingValue);
    }

    private static string NormalizeManualReferenceId(
        string? value,
        bool isSystemGenerated,
        string? existingValue)
    {
        if (isSystemGenerated)
            return NormalizeRequired(value, nameof(value), 80);
        return NormalizeOptional(value, 80)
            ?? (string.IsNullOrWhiteSpace(existingValue) ? Guid.NewGuid().ToString("N") : existingValue);
    }

    private static string NormalizeRequired(string? value, string fieldName, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException($"{fieldName} is required.");
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException(
                $"{fieldName} exceeds maximum length of {maxLength}.");
        return trimmed;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException($"Field exceeds maximum length of {maxLength}.");
        return trimmed;
    }

    private static bool HasAllowedScale(decimal value) =>
        value == 0 || decimal.Round(value, AccountingScale) == value;
}
