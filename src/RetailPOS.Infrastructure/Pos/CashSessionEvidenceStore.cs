using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

public sealed class CashSessionEvidenceStore(RetailPOSDbContext dbContext) : ICashSessionEvidenceStore
{
    public Task<CashSessionHandover?> FindHandoverAsync(long sessionId, long handoverId, CancellationToken cancellationToken) =>
        dbContext.CashSessionHandovers.AsNoTracking().FirstOrDefaultAsync(
            handover => handover.Id == handoverId && handover.CashSessionId == sessionId,
            cancellationToken);

    public async Task<bool> ConfirmHandoverAsync(
        long handoverId,
        long actorUserId,
        DateTime confirmedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var tracked = await dbContext.CashSessionHandovers.FirstOrDefaultAsync(
                handover => handover.Id == handoverId && handover.Status != CashHandoverStatuses.Confirmed,
                cancellationToken);
            if (tracked is null)
                return false;
            tracked.Status = CashHandoverStatuses.Confirmed;
            tracked.ConfirmedAtUtc = confirmedAtUtc;
            tracked.ConfirmedByUserId = actorUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        return await dbContext.CashSessionHandovers
            .Where(handover => handover.Id == handoverId && handover.Status != CashHandoverStatuses.Confirmed)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(handover => handover.Status, CashHandoverStatuses.Confirmed)
                    .SetProperty(handover => handover.ConfirmedAtUtc, confirmedAtUtc)
                    .SetProperty(handover => handover.ConfirmedByUserId, actorUserId),
                cancellationToken) == 1;
    }

    public Task AddAttachmentAsync(CashSessionAttachment attachment, CancellationToken cancellationToken)
    {
        dbContext.CashSessionAttachments.Add(attachment);
        return Task.CompletedTask;
    }

    public Task<CashSessionAttachment?> FindAttachmentAsync(long sessionId, long attachmentId, CancellationToken cancellationToken) =>
        dbContext.CashSessionAttachments.AsNoTracking().FirstOrDefaultAsync(
            attachment => attachment.Id == attachmentId && attachment.CashSessionId == sessionId,
            cancellationToken);

    public Task<int> CountAttachmentsAsync(long sessionId, CancellationToken cancellationToken) =>
        dbContext.CashSessionAttachments.CountAsync(attachment => attachment.CashSessionId == sessionId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}

public sealed record CashSessionAttachmentStorageOptions(string RootPath);

public sealed class CashSessionAttachmentFiles(CashSessionAttachmentStorageOptions options) : ICashSessionAttachmentFiles
{
    public async Task<string> SaveAsync(
        long businessId,
        long sessionId,
        string storedFileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        if (storedFileName != Path.GetFileName(storedFileName))
            throw new ArgumentException("The stored file name must not contain path segments.", nameof(storedFileName));

        var relative = $"{businessId}/{sessionId}/{storedFileName}";
        var physical = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await File.WriteAllBytesAsync(physical, content, cancellationToken);
        return relative;
    }

    public Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(Resolve(storagePath), cancellationToken);

    // The resolved path must stay inside the storage root, whatever the persisted value contains.
    private string Resolve(string relative)
    {
        var root = Path.GetFullPath(options.RootPath);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Attachment path escapes the storage root.");
        return full;
    }
}
