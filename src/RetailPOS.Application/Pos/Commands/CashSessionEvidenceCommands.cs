using System.Security.Cryptography;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos.Commands;

/// <summary>FR-CLOSE-04: a deposit awaiting the bank (or cash awaiting acknowledgement) becomes confirmed, never earlier.</summary>
public sealed record ConfirmCashHandoverCommand(long SessionId, long HandoverId, long ActorUserId)
    : ICommand<CashSessionHandover>;

public sealed class ConfirmCashHandoverCommandHandler(
    ICashSessionStore sessions,
    ICashSessionEvidenceStore evidence,
    ICashSessionWriteOutletAuthorizer outletAuthorizer,
    ICashSessionEventWriter eventWriter,
    ICashSessionAuditWriter auditWriter,
    TimeProvider clock) : IRequestHandler<ConfirmCashHandoverCommand, CashSessionHandover>
{
    public async Task<CashSessionHandover> Handle(ConfirmCashHandoverCommand request, CancellationToken cancellationToken)
    {
        var outletId = await sessions.GetOutletIdAsync(request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
        await outletAuthorizer.AuthorizeWriteAsync(outletId, cancellationToken);

        var handover = await evidence.FindHandoverAsync(request.SessionId, request.HandoverId, cancellationToken)
            ?? throw new CashSessionException(CashSessionErrorCodes.HandoverNotFound, "Handover not found.", 404);
        if (handover.Status == CashHandoverStatuses.Confirmed)
            return handover;

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await sessions.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            if (!await evidence.ConfirmHandoverAsync(handover.Id, request.ActorUserId, now, cancellationToken))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                committed = true;
                return (await evidence.FindHandoverAsync(request.SessionId, request.HandoverId, cancellationToken))!;
            }

            await eventWriter.AppendAsync(
                new CashSessionEvent(
                    CashSessionEventTypes.HandoverConfirmed,
                    handover.BusinessId,
                    "CashSession",
                    request.SessionId.ToString(),
                    $"handover-confirmed:{handover.Id}",
                    now,
                    new
                    {
                        businessId = handover.BusinessId,
                        cashSessionId = request.SessionId,
                        handoverId = handover.Id,
                        method = handover.Method,
                        amount = handover.Amount,
                        confirmedByUserId = request.ActorUserId
                    }),
                cancellationToken);
            await evidence.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        await auditWriter.RecordAsync(
            "CashHandoverConfirmed",
            $"Handover {handover.Id} of session {request.SessionId} confirmed",
            request.SessionId,
            new Dictionary<string, object?>
            {
                ["handoverId"] = handover.Id,
                ["method"] = handover.Method,
                ["amount"] = handover.Amount,
                ["previousStatus"] = handover.Status,
                ["confirmedByUserId"] = request.ActorUserId
            },
            cancellationToken);
        return (await evidence.FindHandoverAsync(request.SessionId, request.HandoverId, cancellationToken))!;
    }
}

public sealed record UploadCashSessionAttachmentCommand(
    long SessionId,
    long? HandoverId,
    string FileName,
    byte[] Content,
    long ActorUserId) : ICommand<CashSessionAttachment>;

public static class CashSessionAttachmentRules
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxPerSession = 20;

    /// <summary>The content type is derived from the file signature; the client-declared type is never trusted.</summary>
    public static (string ContentType, string Extension)? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8)) return ("application/pdf", ".pdf");
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ("image/png", ".png");
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ("image/jpeg", ".jpg");
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return ("image/webp", ".webp");
        return null;
    }

    public static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        if (string.IsNullOrWhiteSpace(name))
            name = "attachment";
        return name.Length <= 200 ? name : name[..200];
    }
}

public sealed class UploadCashSessionAttachmentCommandHandler(
    ICashSessionStore sessions,
    ICashSessionEvidenceStore evidence,
    ICashSessionAttachmentFiles files,
    ICashSessionWriteOutletAuthorizer outletAuthorizer,
    ICashSessionAuditWriter auditWriter,
    TimeProvider clock) : IRequestHandler<UploadCashSessionAttachmentCommand, CashSessionAttachment>
{
    public async Task<CashSessionAttachment> Handle(UploadCashSessionAttachmentCommand request, CancellationToken cancellationToken)
    {
        var session = await sessions.FindAsync(request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
        await outletAuthorizer.AuthorizeWriteAsync(session.OutletId, cancellationToken);
        var businessId = session.BusinessId ?? throw new InvalidOperationException("Cash session has no Business scope.");

        if (request.Content.Length == 0 || request.Content.Length > CashSessionAttachmentRules.MaxBytes)
            throw new CashSessionException(
                CashSessionErrorCodes.AttachmentInvalid,
                $"An attachment must be between 1 byte and {CashSessionAttachmentRules.MaxBytes / (1024 * 1024)} MB.");
        var sniffed = CashSessionAttachmentRules.Sniff(request.Content)
            ?? throw new CashSessionException(
                CashSessionErrorCodes.AttachmentInvalid,
                "Only PDF, JPEG, PNG and WebP files are accepted.");
        if (await evidence.CountAttachmentsAsync(session.Id, cancellationToken) >= CashSessionAttachmentRules.MaxPerSession)
            throw new CashSessionException(CashSessionErrorCodes.AttachmentInvalid, "This session already has the maximum number of attachments.");
        if (request.HandoverId.HasValue
            && await evidence.FindHandoverAsync(session.Id, request.HandoverId.Value, cancellationToken) is null)
            throw new CashSessionException(CashSessionErrorCodes.HandoverNotFound, "Handover not found.", 404);

        var storedName = $"{Guid.NewGuid():N}{sniffed.Extension}";
        var path = await files.SaveAsync(businessId, session.Id, storedName, request.Content, cancellationToken);
        var attachment = new CashSessionAttachment
        {
            BusinessId = businessId,
            OutletId = session.OutletId,
            CashSessionId = session.Id,
            HandoverId = request.HandoverId,
            FileName = CashSessionAttachmentRules.SanitizeFileName(request.FileName),
            ContentType = sniffed.ContentType,
            SizeBytes = request.Content.Length,
            StoragePath = path,
            Sha256 = Convert.ToHexString(SHA256.HashData(request.Content)),
            UploadedByUserId = request.ActorUserId,
            UploadedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        await evidence.AddAttachmentAsync(attachment, cancellationToken);
        await evidence.SaveChangesAsync(cancellationToken);

        await auditWriter.RecordAsync(
            "CashSessionAttachmentAdded",
            $"Attachment added to cash session {session.Id}",
            session.Id,
            new Dictionary<string, object?>
            {
                ["attachmentId"] = attachment.Id,
                ["sha256"] = attachment.Sha256,
                ["sizeBytes"] = attachment.SizeBytes,
                ["handoverId"] = attachment.HandoverId
            },
            cancellationToken);
        return attachment;
    }
}

public sealed record DownloadCashSessionAttachmentQuery(long SessionId, long AttachmentId)
    : IQuery<CashSessionAttachmentContent>;

public sealed class DownloadCashSessionAttachmentQueryHandler(
    ICashSessionStore sessions,
    ICashSessionEvidenceStore evidence,
    ICashSessionAttachmentFiles files,
    ICashSessionOutletAuthorizer outletAuthorizer)
    : IRequestHandler<DownloadCashSessionAttachmentQuery, CashSessionAttachmentContent>
{
    public async Task<CashSessionAttachmentContent> Handle(DownloadCashSessionAttachmentQuery request, CancellationToken cancellationToken)
    {
        var outletId = await sessions.GetOutletIdAsync(request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Cash session not found.");
        await outletAuthorizer.AuthorizeReadAsync(outletId, cancellationToken);

        var attachment = await evidence.FindAttachmentAsync(request.SessionId, request.AttachmentId, cancellationToken)
            ?? throw new KeyNotFoundException("Attachment not found.");
        var bytes = await files.ReadAsync(attachment.StoragePath, cancellationToken);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), attachment.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stored attachment failed its integrity check.");
        return new CashSessionAttachmentContent(attachment.FileName, attachment.ContentType, bytes);
    }
}
