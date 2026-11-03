using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingJournals;

public sealed class JournalNumberAllocator : IJournalNumberAllocator
{
    private readonly RetailPOSDbContext _db;

    public JournalNumberAllocator(RetailPOSDbContext db)
    {
        _db = db;
    }

    public async Task<string> AllocateNextJournalNumberAsync(long businessId, long? financialYearId, DateTime postingDate, long? actorUserId = null, CancellationToken cancellationToken = default)
    {
        var configuration = await _db.AccountingConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken);

        var prefix = string.IsNullOrWhiteSpace(configuration?.JournalNumberPrefix) ? "JR" : configuration!.JournalNumberPrefix.Trim().ToUpperInvariant();
        var padding = Math.Clamp(configuration?.JournalNumberPadding ?? 6, 3, 12);
        var resetPolicy = NormalizeResetPolicy(configuration?.JournalSequenceResetPolicy);

        if (resetPolicy == AccountingSequenceResetPolicies.PerFinancialYear && !financialYearId.HasValue)
        {
            throw new InvalidOperationException("A financial year is required before allocating a journal number with per-year reset policy.");
        }

        var yearCode = await ResolveYearCodeAsync(businessId, financialYearId, postingDate, cancellationToken);
        var scopeKey = resetPolicy == AccountingSequenceResetPolicies.PerFinancialYear
            ? $"fy:{financialYearId!.Value}"
            : "continuous";

        var currentTransaction = _db.Database.CurrentTransaction;
        if (currentTransaction == null)
        {
            throw new InvalidOperationException("Journal number allocation requires an active database transaction.");
        }

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = currentTransaction.GetDbTransaction();
        command.CommandText = @"
INSERT INTO accounting_number_sequences
    (business_id, sequence_type, financial_year_id, scope_key, prefix, padding, last_issued_number, reset_policy, created_at, created_by, updated_at, updated_by)
VALUES
    (@business_id, @sequence_type, @financial_year_id, @scope_key, @prefix, @padding, 1, @reset_policy, @created_at, @created_by, @updated_at, @updated_by)
ON CONFLICT (business_id, sequence_type, scope_key)
DO UPDATE SET
    last_issued_number = accounting_number_sequences.last_issued_number + 1,
    prefix = EXCLUDED.prefix,
    padding = EXCLUDED.padding,
    reset_policy = EXCLUDED.reset_policy,
    updated_at = EXCLUDED.updated_at,
    updated_by = EXCLUDED.updated_by
RETURNING last_issued_number;";

        command.Parameters.Add(new NpgsqlParameter("business_id", businessId));
        command.Parameters.Add(new NpgsqlParameter("sequence_type", AccountingSequenceTypes.Journal));
        command.Parameters.Add(new NpgsqlParameter("financial_year_id", financialYearId.HasValue ? financialYearId.Value : DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("scope_key", scopeKey));
        command.Parameters.Add(new NpgsqlParameter("prefix", prefix));
        command.Parameters.Add(new NpgsqlParameter("padding", padding));
        command.Parameters.Add(new NpgsqlParameter("reset_policy", resetPolicy));
        command.Parameters.Add(new NpgsqlParameter("created_at", DateTime.UtcNow));
        command.Parameters.Add(new NpgsqlParameter("created_by", actorUserId.HasValue ? actorUserId.Value : DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("updated_at", DateTime.UtcNow));
        command.Parameters.Add(new NpgsqlParameter("updated_by", actorUserId.HasValue ? actorUserId.Value : DBNull.Value));

        var nextValue = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return $"{prefix}-{yearCode}-{nextValue.ToString().PadLeft(padding, '0')}";
    }

    private async Task<string> ResolveYearCodeAsync(long businessId, long? financialYearId, DateTime postingDate, CancellationToken cancellationToken)
    {
        if (financialYearId.HasValue)
        {
            var yearCode = await _db.FinancialYears
                .Where(x => x.BusinessId == businessId && x.Id == financialYearId.Value)
                .Select(x => x.YearCode)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(yearCode))
            {
                return yearCode;
            }
        }

        return postingDate.Year.ToString();
    }

    private static string NormalizeResetPolicy(string? value)
    {
        if (string.Equals(value, AccountingSequenceResetPolicies.Continuous, StringComparison.OrdinalIgnoreCase))
        {
            return AccountingSequenceResetPolicies.Continuous;
        }

        return AccountingSequenceResetPolicies.PerFinancialYear;
    }
}