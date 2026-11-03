using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos.Abstractions;

public interface ICashSessionEvidenceStore
{
    Task<CashSessionHandover?> FindHandoverAsync(long sessionId, long handoverId, CancellationToken cancellationToken);

    /// <summary>
    /// Conditional <c>UPDATE … WHERE status &lt;&gt; 'confirmed'</c> touching only the status columns; the amount and the
    /// closing snapshot are never modified. Returns false when the handover was already confirmed.
    /// </summary>
    Task<bool> ConfirmHandoverAsync(
        long handoverId,
        long actorUserId,
        DateTime confirmedAtUtc,
        CancellationToken cancellationToken);

    Task AddAttachmentAsync(CashSessionAttachment attachment, CancellationToken cancellationToken);

    Task<CashSessionAttachment?> FindAttachmentAsync(long sessionId, long attachmentId, CancellationToken cancellationToken);

    Task<int> CountAttachmentsAsync(long sessionId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record CashSessionAttachmentContent(string FileName, string ContentType, byte[] Bytes);

/// <summary>Private, Business-scoped storage outside any web-served directory.</summary>
public interface ICashSessionAttachmentFiles
{
    Task<string> SaveAsync(long businessId, long sessionId, string storedFileName, byte[] content, CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken);
}
