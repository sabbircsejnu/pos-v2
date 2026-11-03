using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Employees;

namespace RetailPOS.Application.HR.Employees;

public sealed class GetHrEmployeeDocumentTypesQueryHandler(
    IHrEmployeeDocumentAccess access,
    IHrEmployeeDocumentStore store)
    : IRequestHandler<GetHrEmployeeDocumentTypesQuery, IReadOnlyList<HrEmployeeDocumentTypeReadModel>>
{
    public async Task<IReadOnlyList<HrEmployeeDocumentTypeReadModel>> Handle(
        GetHrEmployeeDocumentTypesQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(
            businessId, null, HrEmployeeDocumentOperation.ViewTypes, null, cancellationToken);
        return await store.GetTypesAsync(businessId, cancellationToken);
    }
}

public sealed class CreateHrEmployeeDocumentTypeCommandHandler(
    IHrEmployeeDocumentAccess access,
    IHrEmployeeDocumentStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CreateHrEmployeeDocumentTypeCommand, HrEmployeeDocumentTypeReadModel>
{
    public async Task<HrEmployeeDocumentTypeReadModel> Handle(
        CreateHrEmployeeDocumentTypeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var data = request.Data ?? throw new ArgumentNullException(nameof(request.Data));
        if (string.IsNullOrWhiteSpace(data.Code) || data.Code.Trim().Length > 50
            || string.IsNullOrWhiteSpace(data.Name) || data.Name.Trim().Length > 150
            || data.Classification is not ("Internal" or "Confidential" or "Restricted")
            || data.RetentionPeriodDays is <= 0)
            throw new ArgumentException("Document type fields are invalid.");
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(
            businessId, null, HrEmployeeDocumentOperation.ManageTypes, data.Classification, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId, "hr.employee_document_type.created", "HR",
                "Created an employee document type.", "EmployeeDocumentType"),
            token => store.CreateTypeAsync(businessId, data, token),
            cancellationToken);
    }
}

public sealed class GetHrEmployeeDocumentsQueryHandler(
    IHrEmployeeDocumentAccess access,
    IHrEmployeeDocumentStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<GetHrEmployeeDocumentsQuery, IReadOnlyList<HrEmployeeDocumentReadModel>>
{
    public async Task<IReadOnlyList<HrEmployeeDocumentReadModel>> Handle(
        GetHrEmployeeDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EmployeeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.EmployeeId));
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(
            businessId, request.EmployeeId, HrEmployeeDocumentOperation.View, null, cancellationToken);
        var documents = await store.GetForEmployeeAsync(businessId, request.EmployeeId, cancellationToken);
        foreach (var document in documents)
            await access.EnsureAsync(
                businessId, request.EmployeeId, HrEmployeeDocumentOperation.View,
                document.Classification, cancellationToken);
        if (documents.Any(document => document.Classification is "Confidential" or "Restricted"))
        {
            access.RequireRealUserId();
            await audit.ExecuteAsync(
                new RequiredAuditOperation(businessId, "hr.employee_document.metadata_accessed", "HR",
                    "Read confidential employee document metadata.", "HrEmployee", request.EmployeeId.ToString()),
                _ => Task.FromResult(true), cancellationToken);
        }
        return documents;
    }
}

public sealed class CreateHrEmployeeDocumentCommandHandler(
    IHrEmployeeDocumentAccess access,
    IHrEmployeeDocumentStore store,
    IRequiredAuditTransaction audit,
    ConfidentialFileService? files = null)
    : IRequestHandler<CreateHrEmployeeDocumentCommand, HrEmployeeDocumentReadModel>
{
    public async Task<HrEmployeeDocumentReadModel> Handle(
        CreateHrEmployeeDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateUpload(request.FileName, request.Content);
        if (files is null)
            throw new ConfidentialStorageUnavailableException();
        var businessId = access.RequireBusinessId();
        var actorId = access.RequireRealUserId();
        var type = (await store.GetTypesAsync(businessId, cancellationToken))
            .SingleOrDefault(item => item.Id == request.DocumentTypeId && item.IsActive)
            ?? throw new KeyNotFoundException("Active document type was not found in the current Business.");
        ValidateMetadata(request.DocumentNumber, request.IssueDate, request.ExpiryDate, request.Notes);
        if (request.ExpiryDate.HasValue && request.IssueDate.HasValue
            && request.ExpiryDate < request.IssueDate)
            throw new ArgumentException("Expiry date cannot precede issue date.");
        await access.EnsureAsync(
            businessId, request.EmployeeId, HrEmployeeDocumentOperation.Upload,
            type.Classification, cancellationToken);
        var document = await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId, "hr.employee_document.created", "HR",
                "Created employee document metadata.", "EmployeeDocument"),
            token => store.CreateAsync(
                businessId, request.EmployeeId, request.DocumentTypeId, actorId, request.DocumentNumber,
                request.IssueDate, request.ExpiryDate, request.Notes, request.IsLegalHold, token),
            cancellationToken);
        DateTimeOffset? expiry = request.ExpiryDate.HasValue
            ? new DateTimeOffset(request.ExpiryDate.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
            : null;
        await files.StageAndCommitVersionAsync(
            new ConfidentialFileUpload(
                businessId, document.Id, 1, ConfidentialFileClassification.EmployeeDocument,
                request.FileName, request.Content, expiry, null, request.ContentType,
                actorId, null, document.IsLegalHold, document.Version),
            cancellationToken);
        return await store.GetAsync(businessId, document.Id, cancellationToken)
            ?? throw new InvalidOperationException("Created employee document could not be reloaded.");
    }

    private static void ValidateUpload(string fileName, Stream content)
    {
        if (content is null || !content.CanRead)
            throw new ArgumentException("Readable uploaded content is required.");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255
            || fileName.Any(char.IsControl) || fileName.Contains('/') || fileName.Contains('\\')
            || fileName.Contains(':') || fileName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("A safe filename without path components is required.");
    }

    private static void ValidateMetadata(
        string? documentNumber, DateOnly? issueDate, DateOnly? expiryDate, string? notes)
    {
        if (documentNumber?.Length > 100 || notes?.Length > 1000
            || (issueDate.HasValue && expiryDate.HasValue && expiryDate < issueDate))
            throw new ArgumentException("Employee document metadata is invalid.");
    }
}

public sealed class ReplaceHrEmployeeDocumentVersionCommandHandler(
    IHrEmployeeDocumentAccess access,
    IHrEmployeeDocumentStore store,
    ConfidentialFileService? files = null)
    : IRequestHandler<ReplaceHrEmployeeDocumentVersionCommand, HrEmployeeDocumentReadModel>
{
    public async Task<HrEmployeeDocumentReadModel> Handle(
        ReplaceHrEmployeeDocumentVersionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId <= 0 || request.ExpectedDocumentVersion <= 0
            || request.Content is null || !request.Content.CanRead
            || string.IsNullOrWhiteSpace(request.FileName) || request.FileName.Length > 255
            || request.FileName.Any(char.IsControl) || request.FileName.Contains('/')
            || request.FileName.Contains('\\') || request.FileName.Contains(':')
            || request.FileName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Replacement upload fields are invalid.");
        if (files is null)
            throw new ConfidentialStorageUnavailableException();
        var businessId = access.RequireBusinessId();
        var actorId = access.RequireRealUserId();
        var document = await store.GetAsync(businessId, request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee document was not found in the current Business.");
        if (document.Version != request.ExpectedDocumentVersion || document.Status == "Archived")
            throw new HrEmployeeDocumentConcurrencyException("Document is stale or archived.");
        await access.EnsureAsync(
            businessId, document.EmployeeId, HrEmployeeDocumentOperation.Replace,
            document.Classification, cancellationToken);
        var previousVersionId = await store.GetCurrentVersionIdAsync(
            businessId, document.Id, cancellationToken);
        var nextVersionNo = await store.GetNextVersionNoAsync(
            businessId, document.Id, cancellationToken);
        DateTimeOffset? expiry = document.ExpiryDate.HasValue
            ? new DateTimeOffset(document.ExpiryDate.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
            : null;
        await files.StageAndCommitVersionAsync(
            new ConfidentialFileUpload(
                businessId, document.Id, nextVersionNo, ConfidentialFileClassification.EmployeeDocument,
                request.FileName, request.Content, expiry, previousVersionId, request.ContentType,
                actorId, null, document.IsLegalHold, document.Version),
            cancellationToken);
        return await store.GetAsync(businessId, document.Id, cancellationToken)
            ?? throw new InvalidOperationException("Replaced employee document could not be reloaded.");
    }
}

public sealed class ArchiveHrEmployeeDocumentCommandHandler(
    IHrEmployeeDocumentAccess access,
    IHrEmployeeDocumentStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<ArchiveHrEmployeeDocumentCommand, HrEmployeeDocumentReadModel?>
{
    public async Task<HrEmployeeDocumentReadModel?> Handle(
        ArchiveHrEmployeeDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId <= 0 || request.ExpectedVersion <= 0
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("Document archive fields are invalid.");
        var businessId = access.RequireBusinessId();
        var document = await store.GetAsync(businessId, request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee document was not found in the current Business.");
        await access.EnsureAsync(
            businessId, document.EmployeeId, HrEmployeeDocumentOperation.Archive,
            document.Classification, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId, "hr.employee_document.archived", "HR",
                "Archived an employee document.", "EmployeeDocument",
                document.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.ArchiveAsync(
                businessId, document.Id, request.ExpectedVersion, token),
            cancellationToken);
    }
}

public sealed class DownloadHrEmployeeDocumentQueryHandler(
    IHrEmployeeDocumentAccess access,
    IHrEmployeeDocumentStore store,
    ConfidentialFileService? files = null)
    : IRequestHandler<DownloadHrEmployeeDocumentQuery, AuthorizedConfidentialFile>
{
    public async Task<AuthorizedConfidentialFile> Handle(
        DownloadHrEmployeeDocumentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.DocumentId));
        if (files is null)
            throw new ConfidentialStorageUnavailableException();
        var businessId = access.RequireBusinessId();
        var document = await store.GetAsync(businessId, request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee document was not found in the current Business.");
        if (document.Status is "Archived" or "Expired" || document.CurrentVersionId is null)
            throw new UnauthorizedAccessException("Employee document is unavailable.");
        await access.EnsureAsync(
            businessId, document.EmployeeId, HrEmployeeDocumentOperation.Download,
            document.Classification, cancellationToken);
        return await files.OpenAuthorizedReadAsync(
            businessId, document.CurrentVersionId.Value, cancellationToken);
    }
}
