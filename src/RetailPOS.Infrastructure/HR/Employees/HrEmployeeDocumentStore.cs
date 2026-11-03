using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Employees;

public sealed class HrEmployeeDocumentStore(RetailPOSDbContext db, TimeProvider clock)
    : IHrEmployeeDocumentStore, IHrEmployeeDocumentReadStore, IConfidentialFileVersionStore
{
    public async Task<IReadOnlyList<HrEmployeeDocumentTypeReadModel>> GetTypesAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        await db.HrDocumentTypes.AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .OrderBy(row => row.Name)
            .Select(row => new HrEmployeeDocumentTypeReadModel(
                row.Id, row.Code, row.Name, row.Classification, row.RequiresExpiryDate,
                row.RetentionPeriodDays, row.RequiresLegalHoldReview, row.IsActive, row.Version))
            .ToArrayAsync(cancellationToken);

    public async Task<HrEmployeeDocumentTypeReadModel> CreateTypeAsync(
        long businessId,
        HrEmployeeDocumentTypeData data,
        CancellationToken cancellationToken)
    {
        var row = new HrDocumentType
        {
            BusinessId = businessId,
            Code = data.Code.Trim().ToUpperInvariant(),
            Name = data.Name.Trim(),
            Classification = data.Classification,
            RequiresExpiryDate = data.RequiresExpiryDate,
            RetentionPeriodDays = data.RetentionPeriodDays,
            RequiresLegalHoldReview = data.RequiresLegalHoldReview,
            IsActive = data.IsActive,
            Version = 1
        };
        db.HrDocumentTypes.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return new HrEmployeeDocumentTypeReadModel(
            row.Id, row.Code, row.Name, row.Classification, row.RequiresExpiryDate,
            row.RetentionPeriodDays, row.RequiresLegalHoldReview, row.IsActive, row.Version);
    }

    public async Task<HrEmployeeDocumentReadModel?> GetAsync(
        long businessId,
        long documentId,
        CancellationToken cancellationToken)
    {
        var row = await db.HrEmployeeDocuments.AsNoTracking()
            .Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == documentId,
                cancellationToken);
        return row is null ? null : await ToReadModelAsync(row, cancellationToken);
    }

    public async Task<IReadOnlyList<HrEmployeeDocumentReadModel>> GetForEmployeeAsync(
        long businessId,
        long employeeId,
        CancellationToken cancellationToken)
    {
        var documents = await db.HrEmployeeDocuments.AsNoTracking()
            .Include(row => row.DocumentType)
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId)
            .OrderBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var result = new List<HrEmployeeDocumentReadModel>(documents.Length);
        foreach (var document in documents)
            result.Add(await ToReadModelAsync(document, cancellationToken));
        return result;
    }

    public async Task<HrEmployeeDocumentReadModel> CreateAsync(
        long businessId,
        long employeeId,
        long documentTypeId,
        long actorUserId,
        string? documentNumber,
        DateOnly? issueDate,
        DateOnly? expiryDate,
        string? notes,
        bool isLegalHold,
        CancellationToken cancellationToken)
    {
        var type = await db.HrDocumentTypes.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == documentTypeId && row.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Active document type was not found in the current Business.");
        if (type.RequiresExpiryDate && !expiryDate.HasValue)
            throw new ArgumentException("This document type requires an expiry date.");
        var row = new HrEmployeeDocument
        {
            BusinessId = businessId,
            EmployeeId = employeeId,
            DocumentTypeId = documentTypeId,
            DocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber.Trim(),
            DocumentType = type,
            IssueDate = issueDate,
            ExpiryDate = expiryDate,
            Status = "Active",
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IsLegalHold = isLegalHold && type.RequiresLegalHoldReview,
            Version = 1
        };
        db.HrEmployeeDocuments.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return await ToReadModelAsync(row, cancellationToken);
    }

    public async Task<HrEmployeeDocumentReadModel?> ArchiveAsync(
        long businessId,
        long documentId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var row = await db.HrEmployeeDocuments.Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == documentId,
                cancellationToken);
        if (row is null) return null;
        if (row.Version != expectedVersion)
            throw new HrEmployeeDocumentConcurrencyException("Employee document version is stale.");
        if (row.Status == "Archived")
            throw new HrEmployeeDocumentConcurrencyException("Employee document is already archived.");
        row.Status = "Archived";
        row.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return await ToReadModelAsync(row, cancellationToken);
    }

    public async Task<int> GetNextVersionNoAsync(
        long businessId, long documentId, CancellationToken cancellationToken)
    {
        var latest = await db.HrEmployeeDocumentVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeDocumentId == documentId)
            .Select(row => (int?)row.VersionNo)
            .MaxAsync(cancellationToken);
        return (latest ?? 0) + 1;
    }

    public Task<Guid?> GetCurrentVersionIdAsync(
        long businessId, long documentId, CancellationToken cancellationToken) =>
        db.HrEmployeeDocuments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == documentId)
            .Select(row => row.CurrentVersionId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> IsCurrentDownloadableVersionAsync(
        long businessId, long employeeDocumentId, Guid versionId, CancellationToken cancellationToken)
    {
        var document = await db.HrEmployeeDocuments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == employeeDocumentId,
                cancellationToken);
        if (document is null || document.Status != "Active" || document.CurrentVersionId != versionId
            || document.ExpiryDate < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            return false;
        return await db.HrEmployeeDocumentVersions.AsNoTracking().AnyAsync(row =>
            row.BusinessId == businessId && row.EmployeeDocumentId == employeeDocumentId
            && row.Id == versionId && row.ScanStatus == "Clean" && row.RevokedAtUtc == null
            && row.StorageKey != null, cancellationToken);
    }

    public async Task AddAsync(ConfidentialFileVersion version, CancellationToken cancellationToken)
    {
        var document = await GetDocumentForVersionAsync(version, cancellationToken);
        if (document.Status == "Archived")
            throw new InvalidOperationException("Archived employee documents cannot receive a new version.");
        if (document.Version != version.ExpectedSubjectVersion)
            throw new HrEmployeeDocumentConcurrencyException("Employee document metadata changed during upload.");
        if (await GetNextVersionNoAsync(version.BusinessId, document.Id, cancellationToken) != version.Version)
            throw new HrEmployeeDocumentConcurrencyException("Employee document version changed during upload.");
        var replacesId = version.ReplacesVersionId;
        if (document.CurrentVersionId != replacesId)
            throw new HrEmployeeDocumentConcurrencyException("Current document version changed during replacement.");

        var versionRow = ToEntity(version, document);
        db.HrEmployeeDocumentVersions.Add(versionRow);
        document.CurrentVersionId = version.Id;
        document.Status = "Active";
        document.Version++;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRejectedAsync(ConfidentialFileVersion version, CancellationToken cancellationToken)
    {
        var document = await GetDocumentForVersionAsync(version, cancellationToken);
        if (await GetNextVersionNoAsync(version.BusinessId, document.Id, cancellationToken) != version.Version)
            throw new HrEmployeeDocumentConcurrencyException("Employee document version changed during scan.");
        db.HrEmployeeDocumentVersions.Add(ToEntity(version, document));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConfidentialFileVersion?> FindAsync(
        long businessId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var row = await db.HrEmployeeDocumentVersions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == versionId,
                cancellationToken);
        if (row is null) return null;
        var document = await db.HrEmployeeDocuments.AsNoTracking()
            .SingleAsync(item => item.BusinessId == businessId && item.Id == row.EmployeeDocumentId,
                cancellationToken);
        var type = await db.HrDocumentTypes.AsNoTracking()
            .SingleAsync(item => item.BusinessId == businessId && item.Id == document.DocumentTypeId,
                cancellationToken);
        return new ConfidentialFileVersion(
            row.Id, row.BusinessId, row.EmployeeDocumentId, row.VersionNo,
            ConfidentialFileClassification.EmployeeDocument, row.StorageKey ?? string.Empty,
            row.OriginalFileName, row.ContentType, row.SizeBytes, row.ContentHash,
            row.EncryptionKeyReference,
            Enum.Parse<ConfidentialFileScanStatus>(row.ScanStatus, ignoreCase: false),
            document.ExpiryDate.HasValue
                ? new DateTimeOffset(document.ExpiryDate.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
                : null,
            row.RevokedAtUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.RevokedAtUtc.Value, DateTimeKind.Utc)) : null,
            row.ReplacesVersionId,
            row.RetainUntilUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(row.RetainUntilUtc.Value, DateTimeKind.Utc))
                : null,
            row.IsLegalHold || document.IsLegalHold || type.RequiresLegalHoldReview,
            row.UploadedByUserId);
    }

    private async Task<HrEmployeeDocument> GetDocumentForVersionAsync(
        ConfidentialFileVersion version,
        CancellationToken cancellationToken) =>
        await db.HrEmployeeDocuments.Include(row => row.DocumentType)
            .SingleOrDefaultAsync(row => row.BusinessId == version.BusinessId
                && row.Id == version.SubjectId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee document was not found in the current Business.");

    private HrEmployeeDocumentVersion ToEntity(
        ConfidentialFileVersion version,
        HrEmployeeDocument document)
    {
        var uploadedAt = clock.GetUtcNow().UtcDateTime;
        var retainUntil = version.RetainUntil?.UtcDateTime;
        if (!retainUntil.HasValue && document.DocumentType.RetentionPeriodDays is { } retentionDays)
            retainUntil = uploadedAt.AddDays(retentionDays);
        return new HrEmployeeDocumentVersion
        {
            Id = version.Id,
            BusinessId = version.BusinessId,
            EmployeeDocumentId = document.Id,
            VersionNo = version.Version,
            StorageKey = string.IsNullOrEmpty(version.StorageKey) ? null : version.StorageKey,
            OriginalFileName = version.FileName,
            ContentType = version.ContentType,
            SizeBytes = version.SizeBytes,
            ContentHash = version.Sha256,
            ScanStatus = version.ScanStatus.ToString(),
            EncryptionKeyReference = version.EncryptionKeyReference,
            UploadedByUserId = version.UploadedByUserId,
            UploadedAtUtc = uploadedAt,
            ReplacesVersionId = version.ReplacesVersionId,
            RetainUntilUtc = retainUntil,
            IsLegalHold = version.IsLegalHold || document.IsLegalHold
        };
    }

    private async Task<HrEmployeeDocumentReadModel> ToReadModelAsync(
        HrEmployeeDocument row,
        CancellationToken cancellationToken)
    {
        var versions = await db.HrEmployeeDocumentVersions.AsNoTracking()
            .Where(version => version.BusinessId == row.BusinessId && version.EmployeeDocumentId == row.Id)
            .OrderByDescending(version => version.VersionNo)
            .Select(version => new HrEmployeeDocumentVersionReadModel(
                version.Id, version.VersionNo, version.OriginalFileName, version.ContentType,
                version.SizeBytes, version.ScanStatus, version.UploadedByUserId, version.UploadedAtUtc,
                version.ReplacesVersionId, version.RetainUntilUtc, version.IsLegalHold, version.RevokedAtUtc))
            .ToArrayAsync(cancellationToken);
        var status = row.Status == "Active" && row.ExpiryDate < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)
            ? "Expired"
            : row.Status;
        return new HrEmployeeDocumentReadModel(
            row.Id, row.EmployeeId, row.DocumentTypeId, row.DocumentType.Code, row.DocumentType.Name,
            row.DocumentType.Classification, row.DocumentNumber, row.IssueDate, row.ExpiryDate, status,
            row.Notes, row.IsLegalHold, row.CurrentVersionId, row.Version, versions);
    }
}
