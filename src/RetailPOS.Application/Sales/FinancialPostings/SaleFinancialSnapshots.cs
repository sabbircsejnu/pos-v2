using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.Sales.FinancialPostings;

public sealed record SaleJournalPaymentSnapshot(long PaymentId, string Method, decimal Amount, long? PaymentAccountId);

/// <summary>Checkout-time tax evidence of one sale line (v2): net of VAT after every discount, and its VAT.</summary>
public sealed record SaleJournalLineSnapshot(
    long SaleItemId,
    long VariantId,
    int Quantity,
    decimal? TaxableAmount,
    decimal? VatAmount,
    decimal? VatRate,
    string? TaxClassification);

/// <summary>
/// Immutable journal intent of a completed sale (D10). Every amount is fixed when the sale commits, so the journal that
/// is posted later reconciles to the sale exactly, whatever happens to prices, costs or settings in between.
/// <see cref="TotalAmount"/> and <see cref="Vat"/> are the checkout's authoritative payable and VAT; net revenue is
/// their difference for inclusive and exclusive pricing alike (P3.2-D01). <see cref="Lines"/> exists from v2.
/// </summary>
public sealed record SaleJournalSnapshot(
    int Version,
    long SaleId,
    string SaleNumber,
    long BusinessId,
    long OutletId,
    long? TerminalId,
    long? CashSessionId,
    long ActorUserId,
    DateTime TransactionAtUtc,
    DateOnly BusinessDate,
    DateOnly SaleDate,
    decimal GrossSales,
    decimal Discount,
    decimal Vat,
    decimal Cost,
    decimal TotalAmount,
    bool TaxInclusive,
    IReadOnlyList<SaleJournalPaymentSnapshot> Payments,
    IReadOnlyList<SaleJournalLineSnapshot>? Lines = null)
{
    /// <summary>Revenue net of VAT and of every discount.</summary>
    public decimal NetRevenue => TotalAmount - Vat;
}

/// <param name="NetRevenue">The returned share of the line's checkout <c>TaxableAmount</c> (v2).</param>
/// <param name="Vat">The returned share of the line's checkout <c>VatAmount</c> (v2).</param>
public sealed record SaleReversalLineSnapshot(
    long SaleItemId,
    long VariantId,
    int Quantity,
    int SoldQuantity = 0,
    decimal? NetRevenue = null,
    decimal? Vat = null,
    decimal Cost = 0m);

public sealed record SaleReversalPaymentSnapshot(long PaymentId, string Method, decimal Amount);

/// <summary>
/// Immutable journal intent of the single void/refund reversal of a sale. From v2: <see cref="RefundAmount"/> is the
/// revenue reversed net of VAT, <see cref="RefundTotal"/> = net + VAT is what the payments are credited with, and the
/// journal is dated <see cref="AccountingDate"/> — the reversal's own date in the effective Outlet/Business time zone
/// (P3.2-D02). <see cref="BusinessDate"/> and <see cref="TransactionAtUtc"/> stay the original sale's.
/// <see cref="MissingEvidence"/> is set when the persisted sale lacks the allocations needed; nothing is guessed.
/// </summary>
public sealed record SaleReversalJournalSnapshot(
    int Version,
    long SaleId,
    string SaleNumber,
    long BusinessId,
    long OutletId,
    long? TerminalId,
    long? CashSessionId,
    string ReversalType,
    long ActorUserId,
    DateTime TransactionAtUtc,
    DateOnly BusinessDate,
    DateTime RequestedAtUtc,
    decimal RefundAmount,
    decimal RefundVat,
    decimal Cost,
    bool TaxInclusive,
    IReadOnlyList<SaleReversalLineSnapshot> Lines,
    IReadOnlyList<SaleReversalPaymentSnapshot> Payments,
    DateOnly? AccountingDate = null,
    string? TimeZoneId = null,
    decimal RefundTotal = 0m,
    string? MissingEvidence = null);

/// <summary>Builds the posting intents from the authoritative checkout snapshots persisted on the sale.</summary>
public static class SaleFinancialIntentFactory
{
    /// <summary>Version of the snapshot schema. The source keys keep their own <c>v1</c> identity.</summary>
    public const int SnapshotVersion = 2;

    private const int SourceKeyVersion = 1;

    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);

    public static string SaleSourceKey(long saleId) => $"sale:{saleId}:posting:v{SourceKeyVersion}";

    public static string ReversalSourceKey(long saleId) => $"sale:{saleId}:reversal:v{SourceKeyVersion}";

    public static SaleFinancialPosting ForSale(Sale sale, long businessId, DateTime nowUtc)
    {
        RequirePersisted(sale, businessId);
        var snapshot = new SaleJournalSnapshot(
            SnapshotVersion,
            sale.Id,
            sale.SaleNumber,
            businessId,
            sale.OutletId,
            sale.TerminalId,
            sale.CashSessionId,
            sale.CashierId,
            Microseconds(sale.SaleDateTime ?? sale.SaleDate),
            DateOnly.FromDateTime(sale.BusinessDate ?? sale.SaleDate),
            DateOnly.FromDateTime(sale.SaleDate),
            MonetaryRounding.ToCurrency(sale.Items.Sum(item => item.Quantity * item.UnitPrice)),
            MonetaryRounding.ToCurrency(sale.Items.Sum(item => item.DiscountAmount) + sale.Discount),
            MonetaryRounding.ToCurrency(sale.Tax),
            MonetaryRounding.ToCurrency(sale.Items.Sum(item => (item.HistoricalUnitCost ?? 0m) * item.Quantity)),
            MonetaryRounding.ToCurrency(sale.TotalAmount),
            sale.Items.Any(item => item.TaxInclusive == true),
            sale.Payments.OrderBy(payment => payment.Id)
                .Select(payment => new SaleJournalPaymentSnapshot(
                    payment.Id,
                    payment.Method,
                    MonetaryRounding.ToCurrency(payment.Amount),
                    payment.PaymentAccountId))
                .ToArray(),
            sale.Items.OrderBy(item => item.Id)
                .Select(item => new SaleJournalLineSnapshot(
                    item.Id,
                    item.VariantId,
                    item.Quantity,
                    item.TaxableAmount,
                    item.VatAmount,
                    item.VatRate,
                    item.TaxClassification))
                .ToArray());
        return Create(sale, businessId, SaleFinancialPostingKinds.Sale, null, SaleSourceKey(sale.Id), sale.CashierId, snapshot, nowUtc);
    }

    /// <param name="occurredAtUtc">Server time of the void/refund, taken inside its transaction.</param>
    /// <param name="timeZoneId">Effective Outlet/Business zone (D9 rule) that turns it into the accounting date.</param>
    public static SaleFinancialPosting ForReversal(
        Sale sale,
        long businessId,
        IReadOnlyList<SaleRefundLine> refundLines,
        string reversalType,
        long actorUserId,
        DateTime occurredAtUtc,
        string timeZoneId = BusinessPosSettings.DefaultTimeZone)
    {
        RequirePersisted(sale, businessId);
        if (reversalType is not (SaleFinancialPostingReversalTypes.Void or SaleFinancialPostingReversalTypes.Refund))
            throw new ArgumentOutOfRangeException(nameof(reversalType));
        if (refundLines.Count == 0)
            throw new InvalidOperationException("A reversal needs at least one returned line.");

        var occurred = Microseconds(occurredAtUtc);
        var missing = MissingReversalEvidence(sale, refundLines);
        var lines = refundLines
            .GroupBy(line => line.SaleItemId)
            .Select(group =>
            {
                var item = sale.Items.Single(saleItem => saleItem.Id == group.Key);
                var quantity = group.Sum(line => line.Quantity);
                return new SaleReversalLineSnapshot(
                    item.Id,
                    item.VariantId,
                    quantity,
                    item.Quantity,
                    missing is null ? Share(item.TaxableAmount!.Value, quantity, item.Quantity) : null,
                    missing is null ? Share(item.VatAmount!.Value, quantity, item.Quantity) : null,
                    MonetaryRounding.ToCurrency((item.HistoricalUnitCost ?? 0m) * quantity));
            })
            .OrderBy(line => line.SaleItemId)
            .ToArray();

        var net = lines.Sum(line => line.NetRevenue ?? 0m);
        var vat = lines.Sum(line => line.Vat ?? 0m);
        var total = net + vat;
        var payments = sale.Payments.OrderBy(payment => payment.Id).ToArray();
        var allocations = missing is null
            ? Allocate(total, payments.Select(payment => payment.Amount).ToArray())
            : new decimal[payments.Length];

        var snapshot = new SaleReversalJournalSnapshot(
            SnapshotVersion,
            sale.Id,
            sale.SaleNumber,
            businessId,
            sale.OutletId,
            sale.TerminalId,
            sale.CashSessionId,
            reversalType,
            actorUserId,
            Microseconds(sale.SaleDateTime ?? sale.SaleDate),
            DateOnly.FromDateTime(sale.BusinessDate ?? sale.SaleDate),
            occurred,
            net,
            vat,
            lines.Sum(line => line.Cost),
            sale.Items.Any(item => item.TaxInclusive == true),
            lines,
            payments.Select((payment, index) => new SaleReversalPaymentSnapshot(payment.Id, payment.Method, allocations[index]))
                .ToArray(),
            DateOnly.FromDateTime(CashSessionTime.BusinessDateFor(occurred, timeZoneId)),
            timeZoneId,
            total,
            missing);
        return Create(sale, businessId, SaleFinancialPostingKinds.Reversal, reversalType, ReversalSourceKey(sale.Id), actorUserId,
            snapshot, occurred);
    }

    public static SaleJournalSnapshot ReadSaleSnapshot(SaleFinancialPosting posting) =>
        Read<SaleJournalSnapshot>(posting, SaleFinancialPostingKinds.Sale);

    public static SaleReversalJournalSnapshot ReadReversalSnapshot(SaleFinancialPosting posting) =>
        Read<SaleReversalJournalSnapshot>(posting, SaleFinancialPostingKinds.Reversal);

    /// <summary>True when the stored snapshot still hashes to the hash recorded at commit time.</summary>
    public static bool IsIntact(SaleFinancialPosting posting) =>
        string.Equals(Hash(posting.SnapshotJson), posting.SnapshotHash, StringComparison.Ordinal);

    public static string Hash(string snapshotJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson))).ToLowerInvariant();

    /// <summary>
    /// The line amounts a reversal needs are the checkout's own line allocations; a returned quantity takes its
    /// proportional share and the whole line returns exactly what was recorded. The sale's lines must reconcile to its
    /// payable, otherwise the evidence is not trustworthy and the reversal is not built from it.
    /// </summary>
    private static string? MissingReversalEvidence(Sale sale, IReadOnlyList<SaleRefundLine> refundLines)
    {
        foreach (var line in refundLines)
        {
            var item = sale.Items.SingleOrDefault(saleItem => saleItem.Id == line.SaleItemId);
            if (item is null)
                return $"Sale line {line.SaleItemId} is not part of sale {sale.Id}.";
            if (item.TaxableAmount is null || item.VatAmount is null)
                return $"Sale line {item.Id} has no recorded net/VAT allocation.";
        }

        if (sale.Items.Any(item => item.TaxableAmount is null || item.VatAmount is null))
            return $"Sale {sale.Id} has lines without a recorded net/VAT allocation.";
        var recorded = sale.Items.Sum(item => item.TaxableAmount!.Value + item.VatAmount!.Value);
        if (recorded != MonetaryRounding.ToCurrency(sale.TotalAmount))
            return $"Line allocations of sale {sale.Id} ({recorded:0.00}) do not reconcile to its payable ({sale.TotalAmount:0.00}).";
        if (sale.Payments.Count == 0 || sale.Payments.Sum(payment => payment.Amount) != MonetaryRounding.ToCurrency(sale.TotalAmount))
            return $"Payment allocations of sale {sale.Id} do not reconcile to its payable.";
        return null;
    }

    private static decimal Share(decimal recorded, int quantity, int soldQuantity) =>
        quantity >= soldQuantity ? recorded : MonetaryRounding.ToCurrency(recorded * quantity / soldQuantity);

    /// <summary>Splits <paramref name="total"/> by weight in whole cents (largest remainder): the parts sum exactly.</summary>
    public static decimal[] Allocate(decimal total, IReadOnlyList<decimal> weights)
    {
        var result = new decimal[weights.Count];
        var weightSum = weights.Sum();
        if (weights.Count == 0 || weightSum <= 0m)
            return result;
        var cents = (long)decimal.Round(total * 100m, 0, MidpointRounding.AwayFromZero);
        var exact = weights.Select(weight => cents * weight / weightSum).ToArray();
        var floors = exact.Select(value => (long)decimal.Floor(value)).ToArray();
        var remainder = cents - floors.Sum();
        foreach (var index in Enumerable.Range(0, weights.Count)
                     .OrderByDescending(index => exact[index] - floors[index]).ThenBy(index => index)
                     .Take((int)remainder))
            floors[index]++;
        for (var index = 0; index < weights.Count; index++)
            result[index] = floors[index] / 100m;
        return result;
    }

    private static SaleFinancialPosting Create<TSnapshot>(
        Sale sale,
        long businessId,
        string kind,
        string? reversalType,
        string sourceKey,
        long actorUserId,
        TSnapshot snapshot,
        DateTime nowUtc)
    {
        var json = JsonSerializer.Serialize(snapshot, SnapshotOptions);
        var now = Microseconds(nowUtc);
        return new SaleFinancialPosting
        {
            BusinessId = businessId,
            SaleId = sale.Id,
            Kind = kind,
            ReversalType = reversalType,
            SourceKey = sourceKey,
            SnapshotJson = json,
            SnapshotHash = Hash(json),
            OutletId = sale.OutletId,
            TerminalId = sale.TerminalId,
            CashSessionId = sale.CashSessionId,
            ActorUserId = actorUserId,
            BusinessDate = (sale.BusinessDate ?? sale.SaleDate).Date,
            Status = SaleFinancialPostingStatuses.Pending,
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private static T Read<T>(SaleFinancialPosting posting, string expectedKind)
    {
        if (!string.Equals(posting.Kind, expectedKind, StringComparison.Ordinal))
            throw new InvalidOperationException($"Posting {posting.Id} is a '{posting.Kind}' posting, not '{expectedKind}'.");
        return JsonSerializer.Deserialize<T>(posting.SnapshotJson, SnapshotOptions)
            ?? throw new InvalidOperationException($"Posting {posting.Id} has an empty snapshot.");
    }

    private static void RequirePersisted(Sale sale, long businessId)
    {
        if (sale.Id <= 0 || businessId <= 0 || (sale.BusinessId.HasValue && sale.BusinessId != businessId))
            throw new InvalidOperationException("A financial posting intent needs a persisted sale with a Business.");
    }

    // PostgreSQL keeps microseconds; trimming here keeps the snapshot equal to what the database returns.
    private static DateTime Microseconds(DateTime value)
    {
        var utc = UtcTime.Normalize(value);
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}
