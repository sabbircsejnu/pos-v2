using RetailPOS.Application.ConfidentialFiles;

namespace RetailPOS.Application.HR.Employees;

public enum HrEmployeeDocumentOperation
{
    View,
    Upload,
    Replace,
    Download,
    Archive,
    ViewTypes,
    ManageTypes
}

public sealed record HrEmployeeDocumentTypeReadModel(
    long Id,
    string Code,
    string Name,
    string Classification,
    bool RequiresExpiryDate,
    int? RetentionPeriodDays,
    bool RequiresLegalHoldReview,
    bool IsActive,
    long Version);

public sealed record HrEmployeeDocumentTypeData(
    string Code,
    string Name,
    string Classification,
    bool RequiresExpiryDate,
    int? RetentionPeriodDays,
    bool RequiresLegalHoldReview,
    bool IsActive);

public sealed record HrEmployeeDocumentVersionReadModel(
    Guid Id,
    int VersionNo,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string ScanStatus,
    long UploadedByUserId,
    DateTime UploadedAtUtc,
    Guid? ReplacesVersionId,
    DateTime? RetainUntilUtc,
    bool IsLegalHold,
    DateTime? RevokedAtUtc);

public sealed record HrEmployeeDocumentReadModel(
    long Id,
    long EmployeeId,
    long DocumentTypeId,
    string DocumentTypeCode,
    string DocumentTypeName,
    string Classification,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    string Status,
    string? Notes,
    bool IsLegalHold,
    Guid? CurrentVersionId,
    long Version,
    IReadOnlyList<HrEmployeeDocumentVersionReadModel> Versions);

public sealed record CreateHrEmployeeDocumentTypeCommand(HrEmployeeDocumentTypeData Data)
    : MediatR.IRequest<HrEmployeeDocumentTypeReadModel>;

public sealed record GetHrEmployeeDocumentTypesQuery
    : MediatR.IRequest<IReadOnlyList<HrEmployeeDocumentTypeReadModel>>;

public sealed record CreateHrEmployeeDocumentCommand(
    long EmployeeId,
    long DocumentTypeId,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    string? Notes,
    bool IsLegalHold,
    string FileName,
    string ContentType,
    Stream Content) : MediatR.IRequest<HrEmployeeDocumentReadModel>;

public sealed record ReplaceHrEmployeeDocumentVersionCommand(
    long DocumentId,
    long ExpectedDocumentVersion,
    string FileName,
    string ContentType,
    Stream Content) : MediatR.IRequest<HrEmployeeDocumentReadModel>;

public sealed record GetHrEmployeeDocumentsQuery(long EmployeeId)
    : MediatR.IRequest<IReadOnlyList<HrEmployeeDocumentReadModel>>;

public sealed record ArchiveHrEmployeeDocumentCommand(
    long DocumentId,
    long ExpectedVersion,
    string Reason) : MediatR.IRequest<HrEmployeeDocumentReadModel?>;

public sealed record DownloadHrEmployeeDocumentQuery(long DocumentId)
    : MediatR.IRequest<AuthorizedConfidentialFile>;

public sealed record HrEmployeeDocumentUploadFile(
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content);

public interface IHrEmployeeDocumentAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsureAsync(
        long businessId,
        long? employeeId,
        HrEmployeeDocumentOperation operation,
        string? classification,
        CancellationToken cancellationToken);
}

public interface IHrEmployeeDocumentStore
{
    Task<IReadOnlyList<HrEmployeeDocumentTypeReadModel>> GetTypesAsync(
        long businessId,
        CancellationToken cancellationToken);
    Task<HrEmployeeDocumentTypeReadModel> CreateTypeAsync(
        long businessId,
        HrEmployeeDocumentTypeData data,
        CancellationToken cancellationToken);
    Task<HrEmployeeDocumentReadModel?> GetAsync(
        long businessId,
        long documentId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<HrEmployeeDocumentReadModel>> GetForEmployeeAsync(
        long businessId,
        long employeeId,
        CancellationToken cancellationToken);
    Task<HrEmployeeDocumentReadModel> CreateAsync(
        long businessId,
        long employeeId,
        long documentTypeId,
        long actorUserId,
        string? documentNumber,
        DateOnly? issueDate,
        DateOnly? expiryDate,
        string? notes,
        bool isLegalHold,
        CancellationToken cancellationToken);
    Task<HrEmployeeDocumentReadModel?> ArchiveAsync(
        long businessId,
        long documentId,
        long expectedVersion,
        CancellationToken cancellationToken);
    Task<int> GetNextVersionNoAsync(long businessId, long documentId, CancellationToken cancellationToken);
    Task<Guid?> GetCurrentVersionIdAsync(long businessId, long documentId, CancellationToken cancellationToken);
}

public interface IHrEmployeeDocumentReadStore
{
    Task<bool> IsCurrentDownloadableVersionAsync(
        long businessId, long employeeDocumentId, Guid versionId, CancellationToken cancellationToken);
}

public sealed class HrEmployeeDocumentConcurrencyException(string message) : Exception(message);

public sealed class ConfidentialStorageUnavailableException()
    : InvalidOperationException("Confidential document storage, encryption and scanning are not configured.");
