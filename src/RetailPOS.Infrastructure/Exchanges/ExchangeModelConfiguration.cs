using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Exchanges;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Exchanges;

/// <summary>Product Exchange persistence (LLD §3). Money is numeric(18,2), cost numeric(18,4), quantities numeric (base units).</summary>
internal static class ExchangeModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExchangePolicyVersion>(entity =>
        {
            entity.ToTable("exchange_policy_versions", t =>
            {
                t.HasCheckConstraint("ck_exchange_policy_versions_values",
                    "version_no >= 1 AND exchange_window_days >= 0"
                    + " AND (replacement_window_days IS NULL OR replacement_window_days >= 0)"
                    + " AND (max_exchange_count_per_lineage IS NULL OR max_exchange_count_per_lineage >= 1)"
                    + " AND (high_value_approval_threshold IS NULL OR high_value_approval_threshold > 0)");
                t.HasCheckConstraint("ck_exchange_policy_versions_phone_mode", "phone_verification_mode IN ('optional','required')");
                t.HasCheckConstraint("ck_exchange_policy_versions_negative_mode",
                    "negative_target_adjustment_mode IN ('track_only','deduct_from_exchange_salesperson')");
            });
            entity.Property(e => e.PhoneVerificationMode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.NegativeTargetAdjustmentMode).HasMaxLength(40).IsRequired();
            entity.Property(e => e.HighValueApprovalThreshold).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.BusinessId, e.VersionNo }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeNumberCounter>(entity =>
        {
            entity.ToTable("exchange_number_counters", t => t.HasCheckConstraint("ck_exchange_number_counters_sequence", "last_sequence >= 1"));
            entity.HasKey(e => new { e.BusinessId, e.OutletId, e.BusinessDate });
            entity.Property(e => e.BusinessDate).HasColumnType("date");
            entity.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Outlet>().WithMany().HasForeignKey(e => e.OutletId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Exchange>(entity =>
        {
            entity.ToTable("exchanges", t =>
            {
                t.HasCheckConstraint("ck_exchanges_status", "status IN ('completed','reversed')");
                t.HasCheckConstraint("ck_exchanges_reason",
                    "reason_code IN ('size_fitting','colour_design','wrong_product_purchased','customer_preference_changed','other')"
                    + " AND (reason_code <> 'other' OR (reason_other_text IS NOT NULL AND length(trim(reason_other_text)) > 0))");
                t.HasCheckConstraint("ck_exchanges_settlement",
                    "settlement_direction IN ('collect','refund','none')"
                    + " AND settlement_amount = abs(net_adjustment_gross)"
                    + " AND ((settlement_direction = 'none') = (net_adjustment_gross = 0))"
                    + " AND ((settlement_direction = 'collect') = (net_adjustment_gross > 0))");
                t.HasCheckConstraint("ck_exchanges_totals",
                    "return_gross = return_net + return_vat AND replacement_gross = replacement_net + replacement_vat"
                    + " AND net_adjustment_gross = replacement_gross - return_gross"
                    + " AND net_adjustment_net = replacement_net - return_net AND net_adjustment_vat = replacement_vat - return_vat");
                t.HasCheckConstraint("ck_exchanges_phone_result", "phone_verification_result IN ('not_required','skipped_walkin','matched_customer_record')");
                t.HasCheckConstraint("ck_exchanges_loyalty_status",
                    "loyalty_status IN ('module_inactive','applied','pending_reconciliation','not_eligible')");
            });
            entity.Property(e => e.ExchangeNumber).HasMaxLength(60).IsRequired();
            entity.Property(e => e.BusinessDate).HasColumnType("date");
            entity.Property(e => e.EligibilityLocalDate).HasColumnType("date");
            entity.Property(e => e.TimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ReasonCode).HasMaxLength(40).IsRequired();
            entity.Property(e => e.ReasonOtherText).HasMaxLength(500);
            entity.Property(e => e.InternalNote).HasMaxLength(1000);
            entity.Property(e => e.PhoneVerificationResult).HasMaxLength(30).IsRequired();
            foreach (var money in new[]
                     {
                         nameof(Exchange.ReturnNet), nameof(Exchange.ReturnVat), nameof(Exchange.ReturnGross),
                         nameof(Exchange.ReplacementNet), nameof(Exchange.ReplacementDiscount), nameof(Exchange.ReplacementVat),
                         nameof(Exchange.ReplacementGross), nameof(Exchange.NetAdjustmentNet), nameof(Exchange.NetAdjustmentVat),
                         nameof(Exchange.NetAdjustmentGross), nameof(Exchange.HighValueAmount), nameof(Exchange.HighValueThresholdApplied),
                         nameof(Exchange.SettlementAmount)
                     })
                entity.Property(money).HasPrecision(18, 2);
            entity.Property(e => e.ReturnCost).HasPrecision(18, 4);
            entity.Property(e => e.ReplacementCost).HasPrecision(18, 4);
            entity.Property(e => e.SettlementDirection).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PolicySnapshot).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.PreviewHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.LoyaltyStatus).HasMaxLength(30).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(120).IsRequired();
            entity.Property(e => e.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.BusinessId, e.ExchangeNumber }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.BusinessDate, e.ReceivingOutletId });
            entity.HasIndex(e => new { e.BusinessId, e.CustomerId, e.CompletedAtUtc });
            entity.HasIndex(e => new { e.BusinessId, e.SalespersonUserId, e.BusinessDate });
            entity.HasIndex(e => e.CashSessionId);
            entity.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Outlet>().WithMany().HasForeignKey(e => e.ReceivingOutletId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany().HasForeignKey(e => e.TerminalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashSession>().WithMany().HasForeignKey(e => e.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.CashierUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.SalespersonUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExchangePolicyVersion>().WithMany().HasForeignKey(e => e.PolicyVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(e => e.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExchangeReversal>().WithOne().HasForeignKey<Exchange>(e => e.ReversalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.SourceInvoices).WithOne().HasForeignKey(e => e.ExchangeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.ReturnLines).WithOne().HasForeignKey(e => e.ExchangeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.ReplacementLines).WithOne(e => e.Exchange).HasForeignKey(e => e.ExchangeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Settlements).WithOne().HasForeignKey(e => e.ExchangeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Approvals).WithOne().HasForeignKey(e => e.ConsumedByExchangeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeSourceInvoice>(entity =>
        {
            entity.ToTable("exchange_source_invoices");
            entity.Property(e => e.InvoiceNumberSnapshot).HasMaxLength(60).IsRequired();
            entity.Property(e => e.SaleBusinessDateSnapshot).HasColumnType("date");
            entity.Property(e => e.PurchaseLocalDate).HasColumnType("date");
            entity.HasIndex(e => new { e.ExchangeId, e.SaleId }).IsUnique();
            entity.HasIndex(e => e.SaleId);
            entity.HasIndex(e => e.InvoiceNumberSnapshot);
            entity.HasOne<Sale>().WithMany().HasForeignKey(e => e.SaleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeReturnLine>(entity =>
        {
            entity.ToTable("exchange_return_lines", t =>
            {
                t.HasCheckConstraint("ck_exchange_return_lines_source",
                    "(source_kind = 'sale_item' AND sale_item_id IS NOT NULL AND sale_id IS NOT NULL AND source_replacement_line_id IS NULL)"
                    + " OR (source_kind = 'exchange_replacement_line' AND source_replacement_line_id IS NOT NULL AND sale_item_id IS NULL)");
                t.HasCheckConstraint("ck_exchange_return_lines_values",
                    "returned_quantity > 0 AND credit_gross = credit_net + credit_vat AND cost_restored >= 0 AND lineage_depth >= 0");
                // EX-05: only sellable goods enter an exchange.
                t.HasCheckConstraint("ck_exchange_return_lines_condition", "condition = 'resellable'");
            });
            entity.Property(e => e.SourceKind).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Condition).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ReturnedQuantity).HasColumnType("numeric");
            entity.Property(e => e.CreditNet).HasPrecision(18, 2);
            entity.Property(e => e.CreditVat).HasPrecision(18, 2);
            entity.Property(e => e.CreditGross).HasPrecision(18, 2);
            entity.Property(e => e.CostRestored).HasPrecision(18, 4);
            entity.HasIndex(e => e.ExchangeId);
            entity.HasIndex(e => e.SaleItemId);
            entity.HasIndex(e => e.SourceReplacementLineId);
            entity.HasOne<Sale>().WithMany().HasForeignKey(e => e.SaleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SaleItem>().WithMany().HasForeignKey(e => e.SaleItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExchangeReplacementLine>().WithMany().HasForeignKey(e => e.SourceReplacementLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
            UomModelConfiguration.ConfigureSnapshot(entity);
        });

        modelBuilder.Entity<ExchangeReplacementLine>(entity =>
        {
            entity.ToTable("exchange_replacement_lines", t =>
            {
                // C-03: a replacement can be re-exchanged at most up to what was issued.
                t.HasCheckConstraint("ck_exchange_replacement_lines_returned_quantity",
                    "issued_quantity > 0 AND returned_quantity >= 0 AND returned_quantity <= issued_quantity");
                t.HasCheckConstraint("ck_exchange_replacement_lines_values",
                    "taxable_amount >= 0 AND vat_amount >= 0 AND historical_unit_cost >= 0 AND lineage_depth >= 1");
            });
            entity.Property(e => e.IssuedQuantity).HasColumnType("numeric");
            entity.Property(e => e.ReturnedQuantity).HasColumnType("numeric");
            entity.Property(e => e.UnitPrice).HasPrecision(18, 4);
            entity.Property(e => e.LineDiscount).HasPrecision(18, 2);
            entity.Property(e => e.InvoiceDiscountShare).HasPrecision(18, 2);
            entity.Property(e => e.TaxableAmount).HasPrecision(18, 2);
            entity.Property(e => e.VatRate).HasPrecision(9, 4);
            entity.Property(e => e.VatAmount).HasPrecision(18, 2);
            entity.Property(e => e.TaxCode).HasMaxLength(20);
            entity.Property(e => e.TaxClassification).HasMaxLength(30);
            entity.Property(e => e.HistoricalUnitCost).HasColumnType("numeric(18,8)");
            entity.Property(e => e.PackagingCost).HasPrecision(18, 4);
            entity.Property(e => e.PackState).HasMaxLength(20);
            entity.Property(e => e.PackCode).HasMaxLength(40);
            entity.Property(e => e.BaseQtyPerPack).HasColumnType("numeric");
            entity.Property(e => e.AppliedRuleName).HasMaxLength(200);
            entity.HasIndex(e => e.ExchangeId);
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Components).WithOne().HasForeignKey(e => e.ReplacementLineId).OnDelete(DeleteBehavior.Restrict);
            UomModelConfiguration.ConfigureSnapshot(entity);
        });

        modelBuilder.Entity<ExchangeReplacementLineComponent>(entity =>
        {
            entity.ToTable("exchange_replacement_line_components", t => t.HasCheckConstraint("ck_exchange_replacement_line_components_values", "quantity > 0 AND value >= 0"));
            entity.Property(e => e.Quantity).HasColumnType("numeric");
            entity.Property(e => e.UnitCost).HasColumnType("numeric(18,8)");
            entity.Property(e => e.Value).HasPrecision(18, 4);
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.ComponentVariantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeLineageLink>(entity =>
        {
            entity.ToTable("exchange_lineage_links");
            entity.HasKey(e => new { e.ReturnLineId, e.ReplacementLineId });
            entity.HasOne<ExchangeReturnLine>().WithMany().HasForeignKey(e => e.ReturnLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExchangeReplacementLine>().WithMany().HasForeignKey(e => e.ReplacementLineId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeSettlement>(entity =>
        {
            entity.ToTable("exchange_settlements", t =>
            {
                t.HasCheckConstraint("ck_exchange_settlements_values",
                    "direction IN ('collect','refund') AND method IN ('cash','card','mobile') AND amount > 0"
                    + " AND (tendered IS NULL OR (method = 'cash' AND direction = 'collect' AND tendered >= amount))");
                // D-09: a non-cash refund is recorded only with its external reference and confirmation.
                t.HasCheckConstraint("ck_exchange_settlements_refund_evidence",
                    "NOT (direction = 'refund' AND method <> 'cash') OR (external_reference IS NOT NULL AND external_confirmed_by_user_id IS NOT NULL AND external_confirmed_at_utc IS NOT NULL)");
            });
            entity.Property(e => e.Direction).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Method).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Tendered).HasPrecision(18, 2);
            entity.Property(e => e.ChangeGiven).HasPrecision(18, 2);
            entity.Property(e => e.ExternalReference).HasMaxLength(120);
            entity.HasIndex(e => e.ExchangeId);
            entity.HasIndex(e => e.ExchangeReversalId);
            entity.HasOne<CashSession>().WithMany().HasForeignKey(e => e.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashSessionMovement>().WithMany().HasForeignKey(e => e.CashSessionMovementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExchangeReversal>().WithMany().HasForeignKey(e => e.ExchangeReversalId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeApprovalFailure>(entity =>
        {
            entity.ToTable("exchange_approval_failures", t =>
                t.HasCheckConstraint("ck_exchange_approval_failures_kind", "subject_kind IN ('supervisor','cashier')"));
            entity.Property(e => e.SubjectKind).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SubjectHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.HasIndex(e => new { e.BusinessId, e.SubjectKind, e.SubjectHash, e.AttemptedAtUtc });
            entity.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeApproval>(entity =>
        {
            entity.ToTable("exchange_approvals", t =>
            {
                t.HasCheckConstraint("ck_exchange_approvals_type", "approval_type IN ('window','chain','high_value','refund')");
                t.HasCheckConstraint("ck_exchange_approvals_status", "status IN ('granted','consumed','expired','revoked')");
                // C-02: the supervisor is never the cashier.
                t.HasCheckConstraint("ck_exchange_approvals_independent", "supervisor_user_id <> cashier_user_id");
                t.HasCheckConstraint("ck_exchange_approvals_basis", "supervisor_outlet_access_basis IN ('assignment','global_entitlement')");
                t.HasCheckConstraint("ck_exchange_approvals_reason", "length(trim(reason)) > 0");
            });
            entity.Property(e => e.ApprovalType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PermissionUsed).HasMaxLength(80).IsRequired();
            entity.Property(e => e.SupervisorOutletAccessBasis).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
            entity.Property(e => e.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.CreatedAtUtc });
            entity.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.CashierUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.SupervisorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExchangeReversal>().WithMany().HasForeignKey(e => e.ConsumedByReversalId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeSalespersonAdjustment>(entity =>
        {
            entity.ToTable("exchange_salesperson_adjustments", t =>
                t.HasCheckConstraint("ck_exchange_salesperson_adjustments_values",
                    "positive_amount_net >= 0 AND negative_amount_net <= 0 AND signed_amount_net = positive_amount_net + negative_amount_net"));
            entity.Property(e => e.BusinessDate).HasColumnType("date");
            entity.Property(e => e.PositiveAmountNet).HasPrecision(18, 2);
            entity.Property(e => e.NegativeAmountNet).HasPrecision(18, 2);
            entity.Property(e => e.SignedAmountNet).HasPrecision(18, 2);
            entity.Property(e => e.TargetEffectAmountNet).HasPrecision(18, 2);
            entity.Property(e => e.NegativeMode).HasMaxLength(40).IsRequired();
            entity.HasIndex(e => new { e.ExchangeId, e.ExchangeReversalId }).IsUnique().AreNullsDistinct(false);
            entity.HasIndex(e => new { e.BusinessId, e.BusinessDate, e.ReceivingOutletId });
            entity.HasIndex(e => new { e.BusinessId, e.SalespersonUserId, e.BusinessDate });
            entity.HasOne<Exchange>().WithMany().HasForeignKey(e => e.ExchangeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExchangeReversal>().WithMany().HasForeignKey(e => e.ExchangeReversalId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeReceiptSnapshot>(entity =>
        {
            entity.ToTable("exchange_receipt_snapshots");
            entity.Property(e => e.Document).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(e => new { e.ExchangeId, e.ExchangeReversalId }).IsUnique().AreNullsDistinct(false);
            entity.HasOne<Exchange>().WithMany().HasForeignKey(e => e.ExchangeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExchangeReversal>().WithMany().HasForeignKey(e => e.ExchangeReversalId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeReceiptPrintHistory>(entity =>
        {
            entity.ToTable("exchange_receipt_print_history");
            entity.Property(e => e.ActionType).HasMaxLength(30).IsRequired();
            entity.HasIndex(e => e.ExchangeId);
            entity.HasOne<Exchange>().WithMany().HasForeignKey(e => e.ExchangeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany().HasForeignKey(e => e.TerminalId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.PrintedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ExchangeReversal>(entity =>
        {
            entity.ToTable("exchange_reversals", t =>
            {
                t.HasCheckConstraint("ck_exchange_reversals_reason", "length(trim(reason)) > 0");
                t.HasCheckConstraint("ck_exchange_reversals_retained_packaging_cost", "retained_packaging_cost >= 0");
            });
            entity.Property(e => e.ReversalNumber).HasMaxLength(70).IsRequired();
            entity.Property(e => e.BusinessDate).HasColumnType("date");
            entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
            entity.Property(e => e.NetAdjustmentGross).HasPrecision(18, 2);
            entity.Property(e => e.RetainedPackagingCost).HasPrecision(18, 4);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(120).IsRequired();
            entity.Property(e => e.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.ExchangeId).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.IdempotencyKey }).IsUnique();
            entity.HasOne<Exchange>().WithMany().HasForeignKey(e => e.ExchangeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashSession>().WithMany().HasForeignKey(e => e.CashSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(e => e.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        ApplyUtcNormalisation(modelBuilder);
    }

    private static readonly ValueConverter<DateTime, DateTime> UtcConverter =
        new(value => UtcTime.Normalize(value), value => UtcTime.Normalize(value));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter =
        new(value => value.HasValue ? UtcTime.Normalize(value.Value) : null, value => value.HasValue ? UtcTime.Normalize(value.Value) : null);

    // Instants are true UTC on read and write whatever the legacy timestamp switch does; calendar dates are excluded.
    private static void ApplyUtcNormalisation(ModelBuilder modelBuilder)
    {
        var calendarDates = new HashSet<string>
        {
            nameof(Exchange.BusinessDate), nameof(Exchange.EligibilityLocalDate),
            nameof(ExchangeSourceInvoice.PurchaseLocalDate), nameof(ExchangeSourceInvoice.SaleBusinessDateSnapshot)
        };
        foreach (var type in new[]
                 {
                     typeof(ExchangePolicyVersion), typeof(ExchangeNumberCounter), typeof(Exchange), typeof(ExchangeSourceInvoice),
                     typeof(ExchangeSettlement), typeof(ExchangeApproval), typeof(ExchangeSalespersonAdjustment),
                     typeof(ExchangeReceiptSnapshot), typeof(ExchangeReceiptPrintHistory), typeof(ExchangeReversal), typeof(ExchangeApprovalFailure)
                 })
        {
            var entity = modelBuilder.Entity(type);
            foreach (var property in entity.Metadata.GetProperties().ToList())
            {
                if (calendarDates.Contains(property.Name) || property.GetColumnType() == "date")
                    continue;
                if (property.ClrType == typeof(DateTime))
                    entity.Property(property.Name).HasConversion(UtcConverter);
                else if (property.ClrType == typeof(DateTime?))
                    entity.Property(property.Name).HasConversion(NullableUtcConverter);
            }
        }
    }
}
