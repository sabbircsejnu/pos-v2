using RetailPOS.Application.BankStatements.ReadModels;

namespace RetailPOS.Application.BankStatements.Abstractions;

public interface IBankStatementImportReadStore
{
    Task<IReadOnlyList<BankStatementImportReadModel>> ListImportsAsync(
        long businessId,
        long? bankAccountId,
        CancellationToken cancellationToken);

    Task<BankStatementImportReadModel?> GetImportAsync(
        long businessId,
        long importId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BankStatementLineReadModel>> ListLinesAsync(
        long businessId,
        long? importId,
        long? bankAccountId,
        DateTime? dateFrom,
        DateTime? dateTo,
        CancellationToken cancellationToken);
}
