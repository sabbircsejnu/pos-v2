using RetailPOS.Application.BankStatements.ReadModels;

namespace RetailPOS.Application.BankStatements.Abstractions;

public interface IBankStatementImportCommandStore
{
    Task<BankStatementImportReadModel> ImportCsvAsync(
        long businessId,
        long actorUserId,
        long bankAccountId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken);
}

public interface IBankStatementImportLocationAuthorizer
{
    Task AuthorizeAsync(
        long outletId,
        CancellationToken cancellationToken);
}

public interface IBankStatementImportTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
