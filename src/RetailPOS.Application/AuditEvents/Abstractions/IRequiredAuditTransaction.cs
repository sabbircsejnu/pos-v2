namespace RetailPOS.Application.AuditEvents.Abstractions;

public sealed record RequiredAuditOperation(
    long BusinessId,
    string ActionType,
    string Module,
    string Summary,
    string? PrimaryEntityType = null,
    string? PrimaryEntityId = null,
    IReadOnlyList<AuditEventEntityWriteRequest>? Evidence = null);

public interface IRequiredAuditTransaction
{
    Task<T> ExecuteAsync<T>(
        RequiredAuditOperation operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);

    Task<T> ExecuteAsync<T>(
        RequiredAuditOperation operation,
        Func<CancellationToken, Task<T>> action,
        Func<T, bool> requiresAudit,
        CancellationToken cancellationToken);
}

public interface IAuditReadAccessScope
{
    Task<AuditReadAccess> GetAsync(CancellationToken cancellationToken);
}

public sealed record AuditReadAccess(
    long? BusinessId,
    long? OutletId,
    bool AllLocations,
    bool CanReadHr,
    bool CanReadPayroll);
