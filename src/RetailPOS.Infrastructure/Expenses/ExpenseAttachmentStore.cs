using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetailPOS.Application.Expenses;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Expenses;

public sealed class ExpenseAttachmentStore(
    RetailPOSDbContext dbContext,
    IExpenseAttachmentFileStore files,
    IExpenseLocationAuthorizer locationAuthorizer,
    IAuditService auditService,
    IOptions<ExpenseAttachmentOptions> options,
    ILogger<ExpenseAttachmentStore> logger) : IExpenseAttachmentStore
{
    public async Task<IReadOnlyList<ExpenseAttachmentReadModel>> ListAsync(
        long businessId,
        long expenseId,
        CancellationToken cancellationToken)
    {
        await RequireAuthorizedExpenseAsync(businessId, expenseId, cancellationToken);
        return await dbContext.ExpenseAttachments
            .AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.ExpenseId == expenseId)
            .OrderBy(item => item.Id)
            .Select(item => new ExpenseAttachmentReadModel(
                item.Id,
                item.FileName,
                item.ContentType,
                item.FileSizeBytes,
                item.CreatedAt,
                item.CreatedBy))
            .ToListAsync(cancellationToken);
    }

    public async Task<ExpenseAttachmentReadModel> UploadAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        Stream content,
        string? fileName,
        CancellationToken cancellationToken)
    {
        var limits = options.Value;
        var expense = await RequireAuthorizedExpenseAsync(businessId, expenseId, cancellationToken);
        EnsureEditable(expense.Status);

        if (content.Length == 0)
            throw new InvalidOperationException("The attachment is empty.");
        if (content.Length > limits.MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"The attachment exceeds the maximum size of {limits.MaxFileSizeBytes} bytes.");

        var safeName = ExpenseAttachmentContent.SanitizeFileName(fileName);
        var extension = Path.GetExtension(safeName);
        var header = new byte[ExpenseAttachmentContent.HeaderLength];
        var headerLength = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var contentType = ExpenseAttachmentContent.ResolveContentType(extension, header.AsSpan(0, headerLength));
        content.Position = 0;

        // The file is written before the transaction so no disk I/O holds the expense row lock.
        var storageKey = await files.SaveAsync(businessId, expenseId, extension, content, cancellationToken);
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await LockExpenseRowAsync(businessId, expenseId, cancellationToken);

            // Re-read under the lock: the status may have moved and parallel uploads count against one limit.
            var currentStatus = await dbContext.Expenses
                .AsNoTracking()
                .Where(item => item.BusinessId == businessId && item.Id == expenseId)
                .Select(item => item.Status)
                .SingleAsync(cancellationToken);
            EnsureEditable(currentStatus);
            var existingCount = await dbContext.ExpenseAttachments.CountAsync(
                item => item.BusinessId == businessId && item.ExpenseId == expenseId,
                cancellationToken);
            if (existingCount >= limits.MaxFilesPerExpense)
                throw new InvalidOperationException(
                    $"An expense can have at most {limits.MaxFilesPerExpense} attachments.");

            var attachment = new ExpenseAttachment
            {
                BusinessId = businessId,
                ExpenseId = expenseId,
                FileName = safeName,
                ContentType = contentType,
                FileSizeBytes = content.Length,
                StorageKey = storageKey,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = actorUserId
            };
            dbContext.ExpenseAttachments.Add(attachment);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Create,
                Module = AuditModule.Accounts,
                Summary = $"Expense attachment uploaded: {attachment.Id}",
                PrimaryEntity = ("Expense", expenseId.ToString()),
                Entities =
                [
                    new AuditEntityInput(
                        "ExpenseAttachment",
                        attachment.Id.ToString(),
                        AuditActionType.Create,
                        [
                            ("ExpenseId", null, expenseId),
                            ("FileName", null, attachment.FileName),
                            ("FileSizeBytes", null, attachment.FileSizeBytes)
                        ])
                ]
            }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ExpenseAttachmentReadModel(
                attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                attachment.FileSizeBytes,
                attachment.CreatedAt,
                attachment.CreatedBy);
        }
        catch
        {
            RemoveFiles([storageKey]);
            throw;
        }
    }

    public async Task<ExpenseAttachmentContentReadModel> GetContentAsync(
        long businessId,
        long expenseId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        await RequireAuthorizedExpenseAsync(businessId, expenseId, cancellationToken);
        var attachment = await FindAttachmentAsync(businessId, expenseId, attachmentId, tracked: false, cancellationToken);
        if (!files.Exists(attachment.StorageKey))
            throw new KeyNotFoundException("The attachment file is no longer available.");

        var content = await files.ReadAllBytesAsync(attachment.StorageKey, cancellationToken);
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Export,
            Module = AuditModule.Accounts,
            Summary = $"Expense attachment downloaded: {attachment.Id}",
            PrimaryEntity = ("Expense", expenseId.ToString()),
            Entities =
            [
                new AuditEntityInput("ExpenseAttachment", attachment.Id.ToString(), AuditActionType.Export)
            ]
        }, cancellationToken);
        return new ExpenseAttachmentContentReadModel(content, attachment.FileName, attachment.ContentType);
    }

    public async Task DeleteAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        await RequireAuthorizedExpenseAsync(businessId, expenseId, cancellationToken);

        string storageKey;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockExpenseRowAsync(businessId, expenseId, cancellationToken);
            var currentStatus = await dbContext.Expenses
                .AsNoTracking()
                .Where(item => item.BusinessId == businessId && item.Id == expenseId)
                .Select(item => item.Status)
                .SingleAsync(cancellationToken);
            var attachment = await FindAttachmentAsync(businessId, expenseId, attachmentId, tracked: true, cancellationToken);
            EnsureEditable(currentStatus);

            storageKey = attachment.StorageKey;
            dbContext.ExpenseAttachments.Remove(attachment);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Delete,
                Module = AuditModule.Accounts,
                Summary = $"Expense attachment deleted: {attachmentId}",
                PrimaryEntity = ("Expense", expenseId.ToString()),
                Entities =
                [
                    new AuditEntityInput(
                        "ExpenseAttachment",
                        attachmentId.ToString(),
                        AuditActionType.Delete,
                        [
                            ("FileName", attachment.FileName, null),
                            ("DeletedBy", null, actorUserId)
                        ])
                ]
            }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        RemoveFiles([storageKey]);
    }

    public async Task<IReadOnlyList<string>> GetStorageKeysAsync(
        long businessId,
        long expenseId,
        CancellationToken cancellationToken) =>
        await dbContext.ExpenseAttachments
            .AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.ExpenseId == expenseId)
            .Select(item => item.StorageKey)
            .ToListAsync(cancellationToken);

    public void RemoveFiles(IReadOnlyList<string> storageKeys)
    {
        foreach (var storageKey in storageKeys)
        {
            try
            {
                files.DeleteIfExists(storageKey);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Expense attachment file {StorageKey} could not be removed", storageKey);
            }
        }
    }

    private async Task<ExpenseLocationRow> RequireAuthorizedExpenseAsync(
        long businessId,
        long expenseId,
        CancellationToken cancellationToken)
    {
        var expense = await dbContext.Expenses
            .AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.Id == expenseId)
            .Select(item => new ExpenseLocationRow(item.OutletId, item.WarehouseId, item.Status))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {expenseId} not found");
        await locationAuthorizer.AuthorizeAsync(expense.OutletId, expense.WarehouseId, cancellationToken);
        return expense;
    }

    private async Task<ExpenseAttachment> FindAttachmentAsync(
        long businessId,
        long expenseId,
        long attachmentId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var query = tracked ? dbContext.ExpenseAttachments : dbContext.ExpenseAttachments.AsNoTracking();
        return await query.FirstOrDefaultAsync(
                item => item.Id == attachmentId && item.BusinessId == businessId && item.ExpenseId == expenseId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Expense attachment with ID {attachmentId} not found");
    }

    private async Task LockExpenseRowAsync(long businessId, long expenseId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM expenses WHERE business_id = {businessId} AND id = {expenseId} FOR UPDATE",
                cancellationToken);
        }
    }

    private static void EnsureEditable(string status)
    {
        if (!string.Equals(status, ExpenseStatuses.Draft, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, ExpenseStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Attachments of an expense in status '{status}' cannot be changed.");
        }
    }

    private sealed record ExpenseLocationRow(long? OutletId, long? WarehouseId, string Status);
}
