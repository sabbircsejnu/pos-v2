using MediatR;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.ReadModels;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record GetJournalAttachmentsQuery(long JournalEntryId)
    : IRequest<IReadOnlyList<JournalAttachmentReadModel>>;

public sealed class GetJournalAttachmentsQueryHandler(
    IJournalAttachmentStore store,
    IJournalAttachmentTenantScope tenantScope)
    : IRequestHandler<GetJournalAttachmentsQuery, IReadOnlyList<JournalAttachmentReadModel>>
{
    public async Task<IReadOnlyList<JournalAttachmentReadModel>> Handle(
        GetJournalAttachmentsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetAttachmentsAsync(businessId, request.JournalEntryId, cancellationToken);
    }
}

public sealed record UploadJournalAttachmentCommand(
    long JournalEntryId,
    Stream? Content,
    string? FileName,
    string? ContentType,
    long FileSizeBytes,
    long ActorUserId) : IRequest<JournalAttachmentReadModel>;

public sealed class UploadJournalAttachmentCommandHandler(
    IJournalAttachmentStore store,
    IJournalAttachmentTenantScope tenantScope)
    : IRequestHandler<UploadJournalAttachmentCommand, JournalAttachmentReadModel>
{
    public async Task<JournalAttachmentReadModel> Handle(
        UploadJournalAttachmentCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null || request.FileSizeBytes <= 0)
            throw new InvalidOperationException("Attachment file is required.");

        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.UploadAsync(
            businessId,
            request.JournalEntryId,
            request.Content,
            request.FileName,
            request.ContentType,
            request.FileSizeBytes,
            request.ActorUserId,
            cancellationToken);
    }
}

public sealed record DownloadJournalAttachmentQuery(long JournalEntryId, long AttachmentId)
    : IRequest<JournalAttachmentDownloadReadModel>;

public sealed class DownloadJournalAttachmentQueryHandler(
    IJournalAttachmentStore store,
    IJournalAttachmentTenantScope tenantScope)
    : IRequestHandler<DownloadJournalAttachmentQuery, JournalAttachmentDownloadReadModel>
{
    public async Task<JournalAttachmentDownloadReadModel> Handle(
        DownloadJournalAttachmentQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.DownloadAsync(
            businessId,
            request.JournalEntryId,
            request.AttachmentId,
            cancellationToken);
    }
}

public sealed record DeleteJournalAttachmentCommand(long JournalEntryId, long AttachmentId) : IRequest;

public sealed class DeleteJournalAttachmentCommandHandler(
    IJournalAttachmentStore store,
    IJournalAttachmentTenantScope tenantScope)
    : IRequestHandler<DeleteJournalAttachmentCommand>
{
    public async Task Handle(
        DeleteJournalAttachmentCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await store.DeleteAsync(
            businessId,
            request.JournalEntryId,
            request.AttachmentId,
            cancellationToken);
    }
}
