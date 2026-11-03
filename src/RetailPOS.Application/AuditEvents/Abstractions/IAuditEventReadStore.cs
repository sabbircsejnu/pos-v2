using RetailPOS.Application.AuditEvents.ReadModels;

namespace RetailPOS.Application.AuditEvents.Abstractions;

public interface IAuditEventReadStore
{
    Task<AuditEventListReadModel> ListAsync(
        AuditEventListFilter filter,
        CancellationToken cancellationToken);

    Task<AuditEventExportReadModel> ExportAsync(
        AuditEventSearchFilter filter,
        int hardLimit,
        CancellationToken cancellationToken);

    Task<AuditEventDetailsReadModel?> FindDetailsAsync(
        long id,
        CancellationToken cancellationToken);
}

public sealed record AuditEventSearchFilter(
    string? Search,
    DateTime? FromDate,
    DateTime? ToDate,
    long? UserId,
    IReadOnlyList<string>? ActionTypes,
    IReadOnlyList<string>? Modules,
    IReadOnlyList<string>? Sources,
    long? OutletId,
    string? Status);

public sealed record AuditEventListFilter(
    AuditEventSearchFilter Search,
    int Page,
    int PageSize);
