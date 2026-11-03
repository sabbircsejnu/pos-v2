using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankStatements.Abstractions;
using RetailPOS.Application.BankStatements.Parsing;
using RetailPOS.Application.BankStatements.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankStatements;

public sealed class BankStatementImportCommandStore(
    RetailPOSDbContext dbContext,
    IBankStatementImportLocationAuthorizer locationAuthorizer,
    BankStatementCsvParser parser) : IBankStatementImportCommandStore
{
    public async Task<BankStatementImportReadModel> ImportCsvAsync(
        long businessId,
        long actorUserId,
        long bankAccountId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only CSV files are supported.");
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existing = await dbContext.BankStatementImports
            .AsNoTracking()
            .SingleOrDefaultAsync(
                import => import.BusinessId == businessId
                    && import.BankAccountId == bankAccountId
                    && import.SourceFileChecksum == checksum,
                cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        var bank = await dbContext.BankAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                account => account.BusinessId == businessId
                    && account.Id == bankAccountId
                    && account.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Active BankAccount not found in this business.");
        if (bank.OutletId.HasValue)
        {
            await locationAuthorizer.AuthorizeAsync(bank.OutletId.Value, cancellationToken);
        }

        buffer.Position = 0;
        var rows = parser.Parse(buffer).Rows;
        var opening = OneBalance(rows.Select(row => row.OpeningBalance), "opening balance");
        var closing = OneBalance(rows.Select(row => row.ClosingBalance), "closing balance");
        var warning = CheckBalances(rows, opening);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var import = new BankStatementImport
            {
                BusinessId = businessId,
                BankAccountId = bankAccountId,
                SourceFileName = Path.GetFileName(fileName),
                SourceFileChecksum = checksum,
                SourceFormat = "csv-v1",
                ImportedAt = DateTime.UtcNow,
                OpeningBalance = opening,
                ClosingBalance = closing,
                ImportedRowCount = rows.Count,
                AcceptedRowCount = rows.Count,
                Status = BankStatementImportStatuses.Pending,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = actorUserId
            };
            dbContext.BankStatementImports.Add(import);
            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (var row in rows)
            {
                dbContext.BankStatementLines.Add(new BankStatementLine
                {
                    BusinessId = businessId,
                    BankAccountId = bankAccountId,
                    BankStatementImportId = import.Id,
                    SourceRowNumber = row.SourceRowNumber,
                    TransactionDate = row.TransactionDate,
                    ValueDate = row.ValueDate,
                    ExternalTransactionReference = row.ExternalReference,
                    Description = row.Description,
                    DebitAmount = row.DebitAmount,
                    CreditAmount = row.CreditAmount,
                    SignedAmount = row.SignedAmount,
                    RunningBalance = row.RunningBalance,
                    Currency = row.Currency,
                    NormalizedSearchText = string.Join(
                        ' ',
                        new[] { row.Description, row.ExternalReference }
                            .Where(value => !string.IsNullOrWhiteSpace(value)))
                        .ToUpperInvariant(),
                    RawReferenceText = row.RawRow
                });
            }

            import.Status = BankStatementImportStatuses.Imported;
            import.ErrorSummary = warning;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(import);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static decimal? OneBalance(IEnumerable<decimal?> values, string label)
    {
        var valuesPresent = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToList();
        if (valuesPresent.Count > 1)
        {
            throw new InvalidOperationException($"Inconsistent {label} values were supplied.");
        }

        return valuesPresent.Count == 0 ? null : valuesPresent[0];
    }

    private static string? CheckBalances(
        IReadOnlyList<ParsedBankStatementRow> rows,
        decimal? opening)
    {
        decimal? expected = opening;
        foreach (var row in rows.Where(value => value.RunningBalance.HasValue))
        {
            if (expected.HasValue
                && Math.Round(expected.Value + row.SignedAmount, 2)
                    != Math.Round(row.RunningBalance!.Value, 2))
            {
                return "Running balance arithmetic inconsistency detected; supplied evidence was preserved.";
            }

            expected = row.RunningBalance;
        }

        return null;
    }

    private static BankStatementImportReadModel Map(BankStatementImport import) =>
        new(
            import.Id,
            import.BusinessId,
            import.BankAccountId,
            import.SourceFileName,
            import.SourceFileChecksum,
            import.SourceFormat,
            import.ImportedAt,
            import.StatementPeriodStart,
            import.StatementPeriodEnd,
            import.OpeningBalance,
            import.ClosingBalance,
            import.ImportedRowCount,
            import.AcceptedRowCount,
            import.RejectedRowCount,
            import.Status,
            import.ErrorSummary,
            import.CreatedBy);
}
