using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Sales;

/// <summary>
/// D13 invoice numbering. On PostgreSQL the counter row is advanced with a single atomic
/// <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> inside the sale's transaction, so the row lock serialises concurrent
/// sales of one Outlet/day, a rolled-back sale gives its number back and an issued number is never reused.
/// </summary>
public sealed class SaleInvoiceNumberAllocator(RetailPOSDbContext dbContext) : ISaleInvoiceNumberAllocator
{
    public async Task<AllocatedInvoiceNumber> AllocateAsync(
        long businessId,
        long outletId,
        DateTime businessDate,
        CancellationToken cancellationToken)
    {
        var prefix = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.Id == outletId && outlet.BusinessId == businessId)
            .Select(outlet => outlet.InvoicePrefix)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(prefix))
            throw new SaleCheckoutValidationException(
                SaleCheckoutErrorCodes.OutletInvoicePrefixMissing,
                "This outlet has no invoice prefix. Set one in the outlet settings before selling.");

        var date = businessDate.Date;
        long sequence;
        if (dbContext.Database.IsNpgsql())
        {
            if (dbContext.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Invoice number allocation requires an active database transaction.");

            var dateOnly = DateOnly.FromDateTime(date);
            var rows = await dbContext.Database
                .SqlQuery<long>($"""
                    INSERT INTO sale_invoice_counters (business_id, outlet_id, business_date, last_sequence)
                    VALUES ({businessId}, {outletId}, {dateOnly}, 1)
                    ON CONFLICT (business_id, outlet_id, business_date)
                    DO UPDATE SET last_sequence = sale_invoice_counters.last_sequence + 1
                    RETURNING last_sequence AS "Value"
                    """)
                .ToListAsync(cancellationToken);
            sequence = rows.Single();
        }
        else
        {
            // Relational/in-memory test providers have no ON CONFLICT upsert; the primary key still guards duplicates.
            var counter = await dbContext.SaleInvoiceCounters.FirstOrDefaultAsync(
                candidate => candidate.BusinessId == businessId
                    && candidate.OutletId == outletId
                    && candidate.BusinessDate == date,
                cancellationToken);
            if (counter is null)
            {
                counter = new SaleInvoiceCounter
                {
                    BusinessId = businessId,
                    OutletId = outletId,
                    BusinessDate = date,
                    LastSequence = 0
                };
                dbContext.SaleInvoiceCounters.Add(counter);
            }

            counter.LastSequence++;
            await dbContext.SaveChangesAsync(cancellationToken);
            sequence = counter.LastSequence;
        }

        return new AllocatedInvoiceNumber(
            AllocatedInvoiceNumber.Render(prefix.Trim(), date, sequence),
            prefix.Trim(),
            sequence,
            date,
            DateTime.UtcNow);
    }
}
