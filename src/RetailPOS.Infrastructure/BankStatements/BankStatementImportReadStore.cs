using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankStatements.Abstractions;
using RetailPOS.Application.BankStatements.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankStatements;

public sealed class BankStatementImportReadStore(RetailPOSDbContext dbContext)
    : IBankStatementImportReadStore
{
    public async Task<IReadOnlyList<BankStatementImportReadModel>> ListImportsAsync(
        long businessId,
        long? bankAccountId,
        CancellationToken cancellationToken) =>
        await dbContext.BankStatementImports
            .AsNoTracking()
            .Where(import =>
                import.BusinessId == businessId
                && (!bankAccountId.HasValue
                    || import.BankAccountId == bankAccountId.Value))
            .OrderByDescending(import => import.ImportedAt)
            .Select(import => new BankStatementImportReadModel(
                import.Id,
                import.BusinessId,
                import.BankAccountId,
                import.SourceFileName,
                import.SourceFileChecksum,
                import.SourceFormat,
                import.ImportedAt,
                null,
                null,
                import.OpeningBalance,
                import.ClosingBalance,
                import.ImportedRowCount,
                import.AcceptedRowCount,
                import.RejectedRowCount,
                import.Status,
                import.ErrorSummary,
                import.CreatedBy))
            .ToListAsync(cancellationToken);

    public async Task<BankStatementImportReadModel?> GetImportAsync(
        long businessId,
        long importId,
        CancellationToken cancellationToken) =>
        await dbContext.BankStatementImports
            .AsNoTracking()
            .Where(import =>
                import.BusinessId == businessId
                && import.Id == importId)
            .Select(import => new BankStatementImportReadModel(
                import.Id,
                import.BusinessId,
                import.BankAccountId,
                import.SourceFileName,
                import.SourceFileChecksum,
                import.SourceFormat,
                import.ImportedAt,
                null,
                null,
                import.OpeningBalance,
                import.ClosingBalance,
                import.ImportedRowCount,
                import.AcceptedRowCount,
                import.RejectedRowCount,
                import.Status,
                import.ErrorSummary,
                import.CreatedBy))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BankStatementLineReadModel>> ListLinesAsync(
        long businessId,
        long? importId,
        long? bankAccountId,
        DateTime? dateFrom,
        DateTime? dateTo,
        CancellationToken cancellationToken)
    {
        var fromInclusive = dateFrom?.Date;
        var toExclusive = dateTo?.Date.AddDays(1);
        return await dbContext.BankStatementLines
            .AsNoTracking()
            .Where(line =>
                line.BusinessId == businessId
                && (!importId.HasValue
                    || line.BankStatementImportId == importId.Value)
                && (!bankAccountId.HasValue
                    || line.BankAccountId == bankAccountId.Value)
                && (!fromInclusive.HasValue
                    || line.TransactionDate >= fromInclusive.Value)
                && (!toExclusive.HasValue
                    || line.TransactionDate < toExclusive.Value))
            .OrderBy(line => line.TransactionDate)
            .ThenBy(line => line.SourceRowNumber)
            .Select(line => new BankStatementLineReadModel(
                line.Id,
                line.BusinessId,
                line.BankAccountId,
                line.BankStatementImportId,
                line.SourceRowNumber,
                line.TransactionDate,
                line.ValueDate,
                line.ExternalTransactionReference,
                line.Description,
                line.DebitAmount,
                line.CreditAmount,
                line.SignedAmount,
                line.RunningBalance,
                line.Currency,
                line.RawReferenceText))
            .ToListAsync(cancellationToken);
    }
}
