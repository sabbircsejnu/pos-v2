using RetailPOS.Application.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests;

/// <summary>A caller fixed to one Business, for exercising Business-scoped stores directly.</summary>
internal sealed class TestBusinessScope(long? businessId = 1) : IBusinessScope
{
    public long? EffectiveBusinessId => businessId;

    public long RequireBusinessId() =>
        businessId ?? throw new InvalidOperationException("A business must be selected.");
}

internal static class TestInvoices
{
    private static long _sequence = 1000;

    /// <summary>A unique, structured invoice number for allocator doubles (a fixed text keeps legacy assertions readable).</summary>
    public static RetailPOS.Application.Sales.Abstractions.AllocatedInvoiceNumber Issue(string? number = null, DateTime? businessDate = null)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var date = (businessDate ?? DateTime.UtcNow).Date;
        return new RetailPOS.Application.Sales.Abstractions.AllocatedInvoiceNumber(
            number ?? RetailPOS.Application.Sales.Abstractions.AllocatedInvoiceNumber.Render("T", date, sequence),
            "T",
            sequence,
            date,
            DateTime.UtcNow);
    }
}

internal static class TestBusinesses
{
    /// <summary>Adds (and saves) the Business rows relational tests need before seeding Business-owned data.</summary>
    public static async Task SeedAsync(RetailPOSDbContext db, params long[] ids)
    {
        foreach (var id in ids.Length == 0 ? [1L] : ids)
        {
            if (!db.Businesses.Local.Any(business => business.Id == id) &&
                !db.Businesses.Any(business => business.Id == id))
            {
                db.Businesses.Add(new Business { Id = id, Name = $"Test Business {id}" });
            }
        }

        await db.SaveChangesAsync();
    }
}
