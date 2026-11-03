using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingJournals;

public sealed class JournalAttachmentStore(
    RetailPOSDbContext dbContext,
    IJournalAttachmentFileStore files,
    IAuditService auditService) : IJournalAttachmentStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".docx", ".xlsx"
    };

    public async Task<IReadOnlyList<JournalAttachmentReadModel>> GetAttachmentsAsync(
        long businessId,
        long journalEntryId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.JournalEntries.AnyAsync(
            item => item.BusinessId == businessId && item.Id == journalEntryId,
            cancellationToken);
        if (!exists)
            ThrowJournalNotFound(journalEntryId);

        return await dbContext.JournalEntryAttachments
            .AsNoTracking()
            .Where(item => item.JournalEntryId == journalEntryId)
            .OrderBy(item => item.Id)
            .Select(item => new JournalAttachmentReadModel(
                item.Id,
                item.FileName,
                item.ContentType,
                item.FileSizeBytes,
                item.CreatedAt,
                item.CreatedBy))
            .ToListAsync(cancellationToken);
    }

    public async Task<JournalAttachmentReadModel> UploadAsync(
        long businessId,
        long journalEntryId,
        Stream content,
        string? fileName,
        string? contentType,
        long fileSizeBytes,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var journal = await dbContext.JournalEntries
            .FirstOrDefaultAsync(
                item => item.BusinessId == businessId && item.Id == journalEntryId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Journal entry with ID {journalEntryId} was not found.");

        if (IsReadOnly(journal.Status))
        {
            throw new InvalidOperationException(
                "Attachments can only be modified while the journal is in a pre-posted editable state.");
        }

        var configuration = await dbContext.AccountingConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.BusinessId == businessId, cancellationToken)
            ?? throw new InvalidOperationException(
                "Accounting configuration is required before managing manual journal attachments.");

        if (fileSizeBytes > configuration.ManualJournalAttachmentMaxBytes)
        {
            throw new InvalidOperationException(
                $"Attachment exceeds the configured maximum size of {configuration.ManualJournalAttachmentMaxBytes} bytes.");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Unsupported attachment file type.");

        var now = DateTime.UtcNow;
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var relativePath = await files.SaveAsync(
            businessId,
            journalEntryId,
            storedFileName,
            content,
            cancellationToken);

        var attachment = new JournalEntryAttachment
        {
            JournalEntryId = journalEntryId,
            FileName = Path.GetFileName(fileName ?? string.Empty),
            StoredFileName = storedFileName,
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType,
            FileSizeBytes = fileSizeBytes,
            RelativePath = relativePath,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId
        };

        dbContext.JournalEntryAttachments.Add(attachment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception persistenceException)
        {
            try
            {
                files.DeleteIfExists(relativePath);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Journal attachment metadata persistence failed and its stored file could not be removed.",
                    persistenceException,
                    cleanupException);
            }

            throw;
        }

        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Create,
            Module = AuditModule.Accounts,
            Summary = $"Journal attachment uploaded: {attachment.Id}",
            PrimaryEntity = ("JournalEntryAttachment", attachment.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "JournalEntryAttachment",
                    attachment.Id.ToString(),
                    AuditActionType.Create,
                    [
                        ("JournalEntryId", null, attachment.JournalEntryId),
                        ("FileName", null, attachment.FileName),
                        ("FileSizeBytes", null, attachment.FileSizeBytes)
                    ])
            ]
        }, cancellationToken);

        return Map(attachment);
    }

    public async Task<JournalAttachmentDownloadReadModel> DownloadAsync(
        long businessId,
        long journalEntryId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await dbContext.JournalEntryAttachments
            .Include(item => item.JournalEntry)
            .FirstOrDefaultAsync(
                item => item.Id == attachmentId
                    && item.JournalEntryId == journalEntryId
                    && item.JournalEntry.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Journal attachment with ID {attachmentId} was not found.");

        if (!files.Exists(attachment.RelativePath))
            throw new FileNotFoundException("Attachment file was not found on disk.");

        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Export,
            Module = AuditModule.Accounts,
            Summary = $"Journal attachment downloaded: {attachment.Id}",
            PrimaryEntity = ("JournalEntryAttachment", attachment.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "JournalEntryAttachment",
                    attachment.Id.ToString(),
                    AuditActionType.Export)
            ]
        }, cancellationToken);

        var content = await files.ReadAllBytesAsync(attachment.RelativePath, cancellationToken);
        return new JournalAttachmentDownloadReadModel(
            content,
            attachment.FileName,
            attachment.ContentType);
    }

    public async Task DeleteAsync(
        long businessId,
        long journalEntryId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await dbContext.JournalEntryAttachments
            .Include(item => item.JournalEntry)
            .FirstOrDefaultAsync(
                item => item.Id == attachmentId
                    && item.JournalEntryId == journalEntryId
                    && item.JournalEntry.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Journal attachment with ID {attachmentId} was not found.");

        if (IsReadOnly(attachment.JournalEntry.Status))
        {
            throw new InvalidOperationException(
                "Attachments can only be deleted while the journal is in a pre-posted editable state.");
        }

        files.DeleteIfExists(attachment.RelativePath);
        dbContext.JournalEntryAttachments.Remove(attachment);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Delete,
            Module = AuditModule.Accounts,
            Summary = $"Journal attachment deleted: {attachment.Id}",
            PrimaryEntity = ("JournalEntryAttachment", attachment.Id.ToString()),
            Entities =
            [
                new AuditEntityInput(
                    "JournalEntryAttachment",
                    attachment.Id.ToString(),
                    AuditActionType.Delete,
                    [
                        ("JournalEntryId", attachment.JournalEntryId, null),
                        ("FileName", attachment.FileName, null)
                    ])
            ]
        }, cancellationToken);
    }

    private static JournalAttachmentReadModel Map(JournalEntryAttachment attachment) =>
        new(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.FileSizeBytes,
            attachment.CreatedAt,
            attachment.CreatedBy);

    private static bool IsReadOnly(string status) =>
        status == JournalEntryStatuses.Posted
        || status == JournalEntryStatuses.Reversed
        || status == JournalEntryStatuses.Cancelled;

    private static void ThrowJournalNotFound(long journalEntryId) =>
        throw new KeyNotFoundException($"Journal entry with ID {journalEntryId} was not found.");
}
