using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Expenses;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Expenses;

/// <summary>
/// Shares the accounting sequence table with journal numbering. The upsert holds the row lock until the surrounding
/// transaction ends, so concurrent creators serialize per day and prefix and a rollback returns the number.
/// </summary>
public sealed class ExpenseNumberAllocator(
    RetailPOSDbContext dbContext,
    ICompanyTimeZoneProvider timeZones,
    TimeProvider clock) : IExpenseNumberAllocator
{
    private const int Padding = 4;

    public async Task<string> AllocateAsync(
        long businessId,
        long? outletId,
        long? warehouseId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Expense number allocation requires an active database transaction.");

        var outlet = outletId.HasValue
            ? await dbContext.Outlets
                .AsNoTracking()
                .Where(item => item.Id == outletId.Value && item.BusinessId == businessId)
                .Select(item => new { item.InvoicePrefix, item.TimeZoneId })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var zoneId = CashSessionTime.ResolveEffectiveTimeZoneId(
            outlet?.TimeZoneId,
            await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken));
        var businessDate = CashSessionTime.BusinessDateFor(clock.GetUtcNow().UtcDateTime, zoneId);
        var prefix = ExpenseNumberFormat.PrefixFor(outletId, outlet?.InvoicePrefix, warehouseId);
        var scopeKey = ExpenseNumberFormat.ScopeKey(businessDate, prefix);

        var sequence = dbContext.Database.IsRelational()
            ? await NextRelationalAsync(businessId, scopeKey, prefix, actorUserId, cancellationToken)
            : await NextInMemoryAsync(businessId, scopeKey, prefix, actorUserId, cancellationToken);
        return ExpenseNumberFormat.Format(businessDate, prefix, sequence);
    }

    private async Task<long> NextRelationalAsync(
        long businessId,
        string scopeKey,
        string prefix,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var issued = await dbContext.Database.SqlQuery<long>($"""
            INSERT INTO accounting_number_sequences
                (business_id, sequence_type, scope_key, prefix, padding, last_issued_number, reset_policy, created_at, created_by, updated_at, updated_by)
            VALUES
                ({businessId}, {AccountingSequenceTypes.ExpenseReference}, {scopeKey}, {prefix}, {Padding}, 1, {AccountingSequenceResetPolicies.PerBusinessDay}, {now}, {actorUserId}, {now}, {actorUserId})
            ON CONFLICT (business_id, sequence_type, scope_key)
            DO UPDATE SET
                last_issued_number = accounting_number_sequences.last_issued_number + 1,
                updated_at = EXCLUDED.updated_at,
                updated_by = EXCLUDED.updated_by
            RETURNING last_issued_number AS "Value"
            """).ToListAsync(cancellationToken);
        return issued.Single();
    }

    // The EF InMemory provider used by legacy HTTP harnesses has no SQL; it cannot exercise concurrency either way.
    private async Task<long> NextInMemoryAsync(
        long businessId,
        string scopeKey,
        string prefix,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.AccountingNumberSequences.FirstOrDefaultAsync(
            item => item.BusinessId == businessId
                && item.SequenceType == AccountingSequenceTypes.ExpenseReference
                && item.ScopeKey == scopeKey,
            cancellationToken);
        if (row is null)
        {
            row = new AccountingNumberSequence
            {
                BusinessId = businessId,
                SequenceType = AccountingSequenceTypes.ExpenseReference,
                ScopeKey = scopeKey,
                Prefix = prefix,
                Padding = Padding,
                ResetPolicy = AccountingSequenceResetPolicies.PerBusinessDay,
                CreatedBy = actorUserId,
                UpdatedBy = actorUserId
            };
            dbContext.AccountingNumberSequences.Add(row);
        }

        row.LastIssuedNumber++;
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.LastIssuedNumber;
    }
}
