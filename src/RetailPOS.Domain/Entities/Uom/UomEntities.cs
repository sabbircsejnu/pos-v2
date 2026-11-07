namespace RetailPOS.Core.Entities.Uom;

public static class UomDimensions
{
    public const string Count = "count";
    public const string Volume = "volume";
    public const string Mass = "mass";
    public const string Length = "length";
    public const string Packaging = "packaging";

    public static readonly IReadOnlyList<string> All = [Count, Volume, Mass, Length, Packaging];
}

/// <summary>A unit definition. A null <see cref="BusinessId"/> is a global seeded unit; every business can read it.</summary>
public class UomUnit
{
    public const string PcsCode = "pcs";
    public const int MaxPrecision = 4;

    public long Id { get; set; }
    public long? BusinessId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Dimension { get; set; } = UomDimensions.Count;
    public int QuantityPrecision { get; set; }
    public bool IsWholeUnit { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public uint XMin { get; set; }
}

/// <summary>An exact physical conversion: 1 From = Numerator / Denominator To. Both units share a dimension.</summary>
public class UomStandardConversion
{
    public long Id { get; set; }
    public long FromUnitId { get; set; }
    public long ToUnitId { get; set; }
    public decimal Numerator { get; set; }
    public decimal Denominator { get; set; } = 1m;
    public bool IsActive { get; set; } = true;

    public virtual UomUnit FromUnit { get; set; } = null!;
    public virtual UomUnit ToUnit { get; set; } = null!;
}

/// <summary>
/// One immutable version of a variant's unit configuration. A variant without any row is a plain <c>pcs</c> variant.
/// The version with a null <see cref="EffectiveToUtc"/> is current. Committed transaction lines keep their own snapshot,
/// so a later version never changes them.
/// </summary>
public class VariantUomConfig
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long VariantId { get; set; }
    public long BaseUnitId { get; set; }
    public int Version { get; set; } = 1;
    public bool PackStateTrackingEnabled { get; set; }
    public bool RepackingEnabled { get; set; }
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveToUtc { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ProductVariant Variant { get; set; } = null!;
    public virtual UomUnit BaseUnit { get; set; } = null!;
    public virtual ICollection<VariantPurchaseUnitOption> PurchaseOptions { get; set; } = new List<VariantPurchaseUnitOption>();
    public virtual ICollection<VariantSellingOption> SellingOptions { get; set; } = new List<VariantSellingOption>();
}

public class VariantPurchaseUnitOption
{
    public long Id { get; set; }
    public long ConfigId { get; set; }
    public string Code { get; set; } = string.Empty;
    public long UnitId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal FactorNumerator { get; set; }
    public decimal FactorDenominator { get; set; } = 1m;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>One purchased unit arrives as one sealed physical pack when pack-state tracking is on.</summary>
    public bool IsSealedPack { get; set; }

    public virtual VariantUomConfig Config { get; set; } = null!;
    public virtual UomUnit Unit { get; set; } = null!;
}

public static class SellingOptionKinds
{
    public const string Pack = "pack";
    public const string Custom = "custom";
}

/// <summary>
/// A way to sell the variant. A <c>pack</c> has a fixed <see cref="BaseQuantity"/> and a price per pack; a <c>custom</c>
/// option sells a measured quantity of <see cref="UnitId"/> at a price per that unit. Both prices stand alone (D01).
/// </summary>
public class VariantSellingOption
{
    public long Id { get; set; }
    public long ConfigId { get; set; }
    public string Kind { get; set; } = SellingOptionKinds.Pack;
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public long? UnitId { get; set; }
    public decimal? BaseQuantity { get; set; }
    public decimal FactorNumerator { get; set; } = 1m;
    public decimal FactorDenominator { get; set; } = 1m;
    public decimal Price { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>When set, this pack sells a sealed stock pack with this code instead of drawing on loose stock.</summary>
    public string? SealedPackCode { get; set; }

    /// <summary>This pack can be produced by repacking and sold from prepared stock.</summary>
    public bool IsPreparedEligible { get; set; }

    /// <summary>Automatic pricing rules may discount this option. Its own price always stays the starting point.</summary>
    public bool ApplyAutomaticPricingRules { get; set; }

    public virtual VariantUomConfig Config { get; set; } = null!;
    public virtual UomUnit? Unit { get; set; }
    public virtual ICollection<SellingOptionComponent> Components { get; set; } = new List<SellingOptionComponent>();
}

/// <summary>A packaging variant consumed per pack sold or prepared (D04).</summary>
public class SellingOptionComponent
{
    public long Id { get; set; }
    public long SellingOptionId { get; set; }
    public long ComponentVariantId { get; set; }
    public decimal Quantity { get; set; }
    public bool IsRequired { get; set; } = true;

    public virtual VariantSellingOption SellingOption { get; set; } = null!;
    public virtual ProductVariant ComponentVariant { get; set; } = null!;
}

public static class PackStates
{
    public const string Loose = "loose";
    public const string Sealed = "sealed";
    public const string Prepared = "prepared";
}

public static class PackStateOperations
{
    public const string GrnReceipt = "grn_receipt";
    public const string Open = "open";
    public const string Sale = "sale";
    public const string SaleReversal = "sale_reversal";
    public const string TransferOut = "transfer_out";
    public const string TransferIn = "transfer_in";
    public const string Adjustment = "adjustment";
    public const string RepackConsume = "repack_consume";
    public const string RepackProduce = "repack_produce";
    public const string RepackReverse = "repack_reverse";
    public const string Initialize = "initialize";
}

/// <summary>
/// The physical allocation of one variant's stock at one location. It explains <c>Inventory.Quantity</c> and is never a
/// second stock figure: the rows of a tracked variant always sum to it. <see cref="CapitalizedValue"/> is packaging value
/// moved into prepared packs by repacking; it is part of inventory value (see <c>IMPLEMENTATION_IMPACT_ANALYSIS.md</c> D-G).
/// </summary>
public class PackStateBalance
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long VariantId { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string State { get; set; } = PackStates.Loose;
    public string PackCode { get; set; } = string.Empty;
    public decimal BaseQtyPerPack { get; set; }
    public int PackCount { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal CapitalizedValue { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public uint XMin { get; set; }

    public virtual ProductVariant Variant { get; set; } = null!;
}

/// <summary>An immutable signed change to one <see cref="PackStateBalance"/>.</summary>
public class PackStateMovement
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long VariantId { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string State { get; set; } = PackStates.Loose;
    public string PackCode { get; set; } = string.Empty;
    public decimal BaseQtyPerPack { get; set; }
    public int PackCountDelta { get; set; }
    public decimal BaseQuantityDelta { get; set; }
    public decimal CapitalizedValueDelta { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public long ReferenceId { get; set; }
    public string? IdempotencyKey { get; set; }
    public long? ActorUserId { get; set; }
    public DateTime BusinessDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class RepackJobStatuses
{
    public const string Draft = "draft";
    public const string Executed = "executed";
    public const string Cancelled = "cancelled";
    public const string Reversed = "reversed";
}

/// <summary>A controlled operation that fills prepared packs from loose stock and packaging (D12). Not manufacturing.</summary>
public class RepackJob
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public long SourceVariantId { get; set; }
    public long ConfigVersionId { get; set; }
    public long OutputOptionId { get; set; }
    public string OutputPackCode { get; set; } = string.Empty;
    public string OutputLabel { get; set; } = string.Empty;
    public decimal PackBaseQuantity { get; set; }
    public int OutputCount { get; set; }
    public decimal ContentBaseQuantity { get; set; }
    public string Status { get; set; } = RepackJobStatuses.Draft;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public decimal? ContentUnitCost { get; set; }
    public decimal? ContentValue { get; set; }
    public decimal? PackagingValue { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? ExecutedBy { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public long? ReversedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    public uint XMin { get; set; }

    public virtual ProductVariant SourceVariant { get; set; } = null!;
    public virtual ICollection<RepackComponentLine> Components { get; set; } = new List<RepackComponentLine>();
}

public class RepackComponentLine
{
    public long Id { get; set; }
    public long RepackJobId { get; set; }
    public long ComponentVariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? Value { get; set; }

    public virtual RepackJob RepackJob { get; set; } = null!;
    public virtual ProductVariant ComponentVariant { get; set; } = null!;
}

/// <summary>Packaging physically consumed by one sale line, so it can be restored exactly on a void or eligible return.</summary>
public class SaleItemComponent
{
    public long Id { get; set; }
    public long SaleItemId { get; set; }
    public long ComponentVariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Value { get; set; }

    public virtual SaleItem SaleItem { get; set; } = null!;
    public virtual ProductVariant ComponentVariant { get; set; } = null!;
}

public static class ReturnConditions
{
    public const string Resellable = "resellable";
    public const string NonResellable = "non_resellable";
}

/// <summary>What was physically assessed and restored for one returned sale line (D08).</summary>
public class SaleReturnLine
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long SaleItemId { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public string Condition { get; set; } = ReturnConditions.Resellable;
    public decimal RestockedQuantity { get; set; }
    public bool PackagingReusable { get; set; }
    public decimal CostRestored { get; set; }
    public long ActorUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The refund transaction this line belongs to.</summary>
    public long? SaleRefundId { get; set; }

    /// <summary>The returned share of the line's checkout net amount.</summary>
    public decimal? NetAmount { get; set; }

    /// <summary>The returned share of the line's checkout VAT.</summary>
    public decimal? VatAmount { get; set; }

    public virtual SaleItem SaleItem { get; set; } = null!;
    public virtual SaleRefund? SaleRefund { get; set; }
}

/// <summary>One refund transaction of a sale. A sale may have several; each posts its own journal exactly once.</summary>
public class SaleRefund
{
    public long Id { get; set; }
    public long? BusinessId { get; set; }
    public long SaleId { get; set; }

    /// <summary>1 for the sale's first refund, then 2, 3... Unique within the sale.</summary>
    public int Sequence { get; set; }

    /// <summary>The refund's reference, for example <c>INV-100-R2</c>.</summary>
    public string RefundNumber { get; set; } = string.Empty;

    public string? IdempotencyKey { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public long ActorUserId { get; set; }
    public long? DispensingTerminalId { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal CostRestored { get; set; }
    public decimal CashAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual Sale Sale { get; set; } = null!;
    public virtual User? Actor { get; set; }
    public virtual ICollection<SaleReturnLine> Lines { get; set; } = new List<SaleReturnLine>();
    public virtual ICollection<SaleRefundPayment> Payments { get; set; } = new List<SaleRefundPayment>();
}

/// <summary>How much of one original payment a refund pays back. Their sum never exceeds the payment.</summary>
public class SaleRefundPayment
{
    public long Id { get; set; }
    public long SaleRefundId { get; set; }
    public long SalePaymentId { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public virtual SaleRefund SaleRefund { get; set; } = null!;
}

/// <summary>The pack-state make-up of one transfer line. Moved as dispatched, so nothing is created or destroyed.</summary>
public class StockTransferItemAllocation
{
    public long Id { get; set; }
    public long TransferItemId { get; set; }
    public string State { get; set; } = PackStates.Loose;
    public string PackCode { get; set; } = string.Empty;
    public decimal BaseQtyPerPack { get; set; }
    public int PackCount { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal CapitalizedValue { get; set; }

    public virtual StockTransferItem TransferItem { get; set; } = null!;
}

/// <summary>One physical count row (<c>2 × Jar500</c>, <c>175 × ml</c>); a count line's physical stock is their sum.</summary>
public class StockCountLineEntry : UomSnapshotColumns
{
    public long Id { get; set; }
    public long StockCountLineId { get; set; }
    public decimal BaseQuantity { get; set; }
    public string? State { get; set; }
    public string? PackCode { get; set; }
    public decimal BaseQtyPerPack { get; set; }
    public int PackCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual StockCountLine StockCountLine { get; set; } = null!;
}

public sealed record UnitTransactionSnapshot(
    string UnitCode,
    string? Label,
    decimal EnteredQuantity,
    decimal? EnteredUnitPrice,
    decimal FactorNumerator,
    decimal FactorDenominator,
    string BaseUnitCode,
    long? ConfigVersionId,
    long? OptionId,
    string? OptionCode);

/// <summary>
/// Immutable unit interpretation of a transaction line (D07). The line's own quantity is always in base units. Null on a
/// line means legacy <c>pcs</c> × 1: such rows are never updated with guessed metadata.
/// </summary>
public abstract class UomSnapshotColumns
{
    public string? UomUnitCode { get; private set; }
    public string? UomLabel { get; private set; }
    public decimal? UomEnteredQuantity { get; private set; }
    public decimal? UomEnteredUnitPrice { get; private set; }
    public decimal? UomFactorNumerator { get; private set; }
    public decimal? UomFactorDenominator { get; private set; }
    public string? UomBaseUnitCode { get; private set; }
    public long? UomConfigVersionId { get; private set; }
    public long? UomOptionId { get; private set; }
    public string? UomOptionCode { get; private set; }

    public void CaptureUomSnapshot(UnitTransactionSnapshot snapshot)
    {
        if (UomUnitCode is not null)
            throw new InvalidOperationException("A committed unit snapshot cannot be changed.");

        UomUnitCode = snapshot.UnitCode;
        UomLabel = snapshot.Label;
        UomEnteredQuantity = snapshot.EnteredQuantity;
        UomEnteredUnitPrice = snapshot.EnteredUnitPrice;
        UomFactorNumerator = snapshot.FactorNumerator;
        UomFactorDenominator = snapshot.FactorDenominator;
        UomBaseUnitCode = snapshot.BaseUnitCode;
        UomConfigVersionId = snapshot.ConfigVersionId;
        UomOptionId = snapshot.OptionId;
        UomOptionCode = snapshot.OptionCode;
    }

    public UnitTransactionSnapshot? ReadUomSnapshot() => UomUnitCode is null
        ? null
        : new UnitTransactionSnapshot(
            UomUnitCode,
            UomLabel,
            UomEnteredQuantity ?? 0m,
            UomEnteredUnitPrice,
            UomFactorNumerator ?? 1m,
            UomFactorDenominator ?? 1m,
            UomBaseUnitCode ?? UomUnit.PcsCode,
            UomConfigVersionId,
            UomOptionId,
            UomOptionCode);
}
