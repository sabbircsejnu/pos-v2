using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Infrastructure.Data;

internal static class UomModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UomUnit>(entity =>
        {
            entity.Property(e => e.Code).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Dimension).HasMaxLength(20).IsRequired();
            entity.HasIndex(e => e.Code).IsUnique().HasFilter("business_id IS NULL")
                .HasDatabaseName("ux_uom_units_global_code");
            entity.HasIndex(e => new { e.BusinessId, e.Code }).IsUnique().HasFilter("business_id IS NOT NULL")
                .HasDatabaseName("ux_uom_units_business_code");
            entity.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UomStandardConversion>(entity =>
        {
            entity.Property(e => e.Numerator).HasColumnType("numeric");
            entity.Property(e => e.Denominator).HasColumnType("numeric");
            entity.HasIndex(e => new { e.FromUnitId, e.ToUnitId }).IsUnique();
            entity.HasOne(e => e.FromUnit).WithMany().HasForeignKey(e => e.FromUnitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ToUnit).WithMany().HasForeignKey(e => e.ToUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VariantUomConfig>(entity =>
        {
            entity.HasIndex(e => new { e.VariantId, e.Version }).IsUnique();
            // One current version per variant, so versions can never overlap.
            entity.HasIndex(e => e.VariantId).IsUnique().HasFilter("effective_to_utc IS NULL")
                .HasDatabaseName("ux_variant_uom_configs_current");
            entity.HasIndex(e => e.BusinessId);
            entity.HasOne(e => e.Variant).WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.BaseUnit).WithMany().HasForeignKey(e => e.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VariantPurchaseUnitOption>(entity =>
        {
            entity.Property(e => e.Code).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Label).HasMaxLength(100).IsRequired();
            entity.Property(e => e.FactorNumerator).HasColumnType("numeric");
            entity.Property(e => e.FactorDenominator).HasColumnType("numeric");
            entity.HasIndex(e => new { e.ConfigId, e.Code }).IsUnique();
            entity.HasOne(e => e.Config).WithMany(c => c.PurchaseOptions).HasForeignKey(e => e.ConfigId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Unit).WithMany().HasForeignKey(e => e.UnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VariantSellingOption>(entity =>
        {
            entity.Property(e => e.Kind).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Code).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Label).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SealedPackCode).HasMaxLength(40);
            entity.Property(e => e.BaseQuantity).HasColumnType("numeric");
            entity.Property(e => e.FactorNumerator).HasColumnType("numeric");
            entity.Property(e => e.FactorDenominator).HasColumnType("numeric");
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.ConfigId, e.Code }).IsUnique();
            entity.HasOne(e => e.Config).WithMany(c => c.SellingOptions).HasForeignKey(e => e.ConfigId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Unit).WithMany().HasForeignKey(e => e.UnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SellingOptionComponent>(entity =>
        {
            entity.Property(e => e.Quantity).HasColumnType("numeric");
            entity.HasIndex(e => new { e.SellingOptionId, e.ComponentVariantId }).IsUnique();
            entity.HasOne(e => e.SellingOption).WithMany(o => o.Components).HasForeignKey(e => e.SellingOptionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ComponentVariant).WithMany().HasForeignKey(e => e.ComponentVariantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PackStateBalance>(entity =>
        {
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.State).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PackCode).HasMaxLength(40).IsRequired();
            entity.Property(e => e.BaseQtyPerPack).HasColumnType("numeric");
            entity.Property(e => e.BaseQuantity).HasColumnType("numeric");
            entity.Property(e => e.CapitalizedValue).HasPrecision(18, 4);
            entity.HasIndex(e => new { e.VariantId, e.LocationId, e.LocationType, e.State, e.PackCode, e.BaseQtyPerPack }).IsUnique()
                .HasDatabaseName("ux_pack_state_balances_identity");
            entity.HasIndex(e => new { e.BusinessId, e.LocationType, e.LocationId });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_pack_state_balances_not_negative",
                    "pack_count >= 0 AND base_quantity >= 0 AND capitalized_value >= 0");
            });
            entity.HasOne(e => e.Variant).WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PackStateMovement>(entity =>
        {
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.OperationType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.State).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PackCode).HasMaxLength(40).IsRequired();
            entity.Property(e => e.ReferenceType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(120);
            entity.Property(e => e.BaseQtyPerPack).HasColumnType("numeric");
            entity.Property(e => e.BaseQuantityDelta).HasColumnType("numeric");
            entity.Property(e => e.CapitalizedValueDelta).HasPrecision(18, 4);
            entity.Property(e => e.BusinessDate).HasColumnType("date");
            entity.HasIndex(e => new { e.VariantId, e.LocationId, e.LocationType, e.CreatedAt });
            entity.HasIndex(e => new { e.ReferenceType, e.ReferenceId });
            entity.HasIndex(e => new { e.BusinessId, e.BusinessDate });
            // An operation's key may cover several signed rows (one per balance), so it is unique per row identity.
            entity.HasIndex(e => new { e.BusinessId, e.IdempotencyKey, e.State, e.PackCode, e.BaseQtyPerPack }).IsUnique()
                .HasFilter("idempotency_key IS NOT NULL")
                .HasDatabaseName("ux_pack_state_movements_idempotency");
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RepackJob>(entity =>
        {
            entity.Property(e => e.Reference).HasMaxLength(40).IsRequired();
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.OutputPackCode).HasMaxLength(40).IsRequired();
            entity.Property(e => e.OutputLabel).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.PackBaseQuantity).HasColumnType("numeric");
            entity.Property(e => e.ContentBaseQuantity).HasColumnType("numeric");
            entity.Property(e => e.ContentUnitCost).HasColumnType("numeric");
            entity.Property(e => e.ContentValue).HasPrecision(18, 4);
            entity.Property(e => e.PackagingValue).HasPrecision(18, 4);
            entity.HasIndex(e => new { e.BusinessId, e.Reference }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.LocationType, e.LocationId, e.CreatedAt });
            entity.HasOne(e => e.SourceVariant).WithMany().HasForeignKey(e => e.SourceVariantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RepackComponentLine>(entity =>
        {
            entity.Property(e => e.Quantity).HasColumnType("numeric");
            entity.Property(e => e.UnitCost).HasColumnType("numeric");
            entity.Property(e => e.Value).HasPrecision(18, 4);
            entity.HasIndex(e => new { e.RepackJobId, e.ComponentVariantId }).IsUnique();
            entity.HasOne(e => e.RepackJob).WithMany(j => j.Components).HasForeignKey(e => e.RepackJobId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ComponentVariant).WithMany().HasForeignKey(e => e.ComponentVariantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleItemComponent>(entity =>
        {
            entity.Property(e => e.Quantity).HasColumnType("numeric");
            entity.Property(e => e.UnitCost).HasColumnType("numeric");
            entity.Property(e => e.Value).HasPrecision(18, 4);
            entity.HasIndex(e => e.SaleItemId);
            entity.HasOne(e => e.SaleItem).WithMany(i => i.Components).HasForeignKey(e => e.SaleItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ComponentVariant).WithMany().HasForeignKey(e => e.ComponentVariantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleReturnLine>(entity =>
        {
            entity.Property(e => e.Condition).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ReturnedQuantity).HasColumnType("numeric");
            entity.Property(e => e.RestockedQuantity).HasColumnType("numeric");
            entity.Property(e => e.CostRestored).HasPrecision(18, 4);
            entity.HasIndex(e => e.SaleId);
            entity.HasIndex(e => e.SaleItemId);
            entity.Property(e => e.NetAmount).HasPrecision(18, 2);
            entity.Property(e => e.VatAmount).HasPrecision(18, 2);
            entity.HasIndex(e => e.SaleRefundId);
            entity.HasOne(e => e.SaleItem).WithMany(i => i.ReturnLines).HasForeignKey(e => e.SaleItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SaleRefund).WithMany(r => r.Lines).HasForeignKey(e => e.SaleRefundId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleRefund>(entity =>
        {
            entity.ToTable("sale_refunds");
            entity.Property(e => e.RefundNumber).HasMaxLength(80).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(120);
            entity.Property(e => e.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.NetAmount).HasPrecision(18, 2);
            entity.Property(e => e.VatAmount).HasPrecision(18, 2);
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.CostRestored).HasPrecision(18, 4);
            entity.Property(e => e.CashAmount).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.SaleId, e.Sequence }).IsUnique();
            entity.HasIndex(e => new { e.SaleId, e.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
            entity.HasOne(e => e.Sale).WithMany(s => s.Refunds).HasForeignKey(e => e.SaleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Actor).WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("ck_sale_refunds_amounts", "total_amount = net_amount + vat_amount AND sequence >= 1"));
        });

        modelBuilder.Entity<SaleRefundPayment>(entity =>
        {
            entity.ToTable("sale_refund_payments");
            entity.Property(e => e.Method).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.SaleRefundId, e.SalePaymentId }).IsUnique();
            entity.HasIndex(e => e.SalePaymentId);
            entity.HasOne(e => e.SaleRefund).WithMany(r => r.Payments).HasForeignKey(e => e.SaleRefundId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RetailPOS.Core.Entities.SalePayment>().WithMany().HasForeignKey(e => e.SalePaymentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockTransferItemAllocation>(entity =>
        {
            entity.Property(e => e.State).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PackCode).HasMaxLength(40).IsRequired();
            entity.Property(e => e.BaseQtyPerPack).HasColumnType("numeric");
            entity.Property(e => e.BaseQuantity).HasColumnType("numeric");
            entity.Property(e => e.CapitalizedValue).HasPrecision(18, 4);
            entity.HasIndex(e => e.TransferItemId);
            entity.HasOne(e => e.TransferItem).WithMany(i => i.Allocations).HasForeignKey(e => e.TransferItemId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockCountLineEntry>(entity =>
        {
            ConfigureSnapshot(entity);
            entity.Property(e => e.State).HasMaxLength(20);
            entity.Property(e => e.PackCode).HasMaxLength(40);
            entity.Property(e => e.BaseQuantity).HasColumnType("numeric");
            entity.Property(e => e.BaseQtyPerPack).HasColumnType("numeric");
            entity.HasIndex(e => e.StockCountLineId);
            entity.HasOne(e => e.StockCountLine).WithMany(l => l.Entries).HasForeignKey(e => e.StockCountLineId).OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureSnapshot(modelBuilder.Entity<PurchaseOrderItem>());
        ConfigureSnapshot(modelBuilder.Entity<GrnItem>());
        ConfigureSnapshot(modelBuilder.Entity<SaleItem>());
        modelBuilder.Entity<SaleItem>(entity =>
        {
            entity.Property(e => e.ReturnedQuantity).HasColumnType("numeric").HasDefaultValue(0m);
            // The database's own guard against returning more than was sold, whatever a request or a race does.
            entity.ToTable(t => t.HasCheckConstraint("ck_sale_items_returned_quantity", "returned_quantity >= 0 AND returned_quantity <= quantity"));
        });
        ConfigureSnapshot(modelBuilder.Entity<StockTransferItem>());
        ConfigureSnapshot(modelBuilder.Entity<StockAdjustmentLine>());

        modelBuilder.Entity<StockAdjustmentLine>(entity =>
        {
            entity.Property(e => e.PackState).HasMaxLength(20);
            entity.Property(e => e.PackCode).HasMaxLength(40);
            entity.Property(e => e.BaseQtyPerPack).HasColumnType("numeric");
        });
    }

    /// <summary>Runs after the snake_case pass, which would otherwise rename the system column.</summary>
    public static void ConfigureConcurrency(ModelBuilder modelBuilder)
    {
        Xmin(modelBuilder.Entity<UomUnit>().Property(e => e.XMin));
        Xmin(modelBuilder.Entity<PackStateBalance>().Property(e => e.XMin));
        Xmin(modelBuilder.Entity<RepackJob>().Property(e => e.XMin));
    }

    private static void Xmin(PropertyBuilder<uint> property) =>
        property.HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

    private static void ConfigureSnapshot<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : UomSnapshotColumns
    {
        entity.Property(e => e.UomUnitCode).HasMaxLength(20);
        entity.Property(e => e.UomLabel).HasMaxLength(100);
        entity.Property(e => e.UomBaseUnitCode).HasMaxLength(20);
        entity.Property(e => e.UomOptionCode).HasMaxLength(40);
        entity.Property(e => e.UomEnteredQuantity).HasColumnType("numeric");
        entity.Property(e => e.UomEnteredUnitPrice).HasPrecision(18, 2);
        entity.Property(e => e.UomFactorNumerator).HasColumnType("numeric");
        entity.Property(e => e.UomFactorDenominator).HasColumnType("numeric");
    }
}
