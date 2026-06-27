using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;

namespace RetailPOS.Infrastructure.Data;

public class RetailPOSDbContext : DbContext
{
    public RetailPOSDbContext(DbContextOptions<RetailPOSDbContext> options) : base(options)
    {
    }

    // Tenancy
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessFeatureSetting> BusinessFeatureSettings => Set<BusinessFeatureSetting>();
    public DbSet<BusinessReminderNotification> BusinessReminderNotifications => Set<BusinessReminderNotification>();

    // User & Role Management
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<UserWarehouseAssignment> UserWarehouseAssignments => Set<UserWarehouseAssignment>();
    public DbSet<UserOutletAssignment> UserOutletAssignments => Set<UserOutletAssignment>();

    // Locations
    public DbSet<Outlet> Outlets => Set<Outlet>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    // Products & Inventory
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Variation> Variations => Set<Variation>();
    public DbSet<VariationOption> VariationOptions => Set<VariationOption>();
    public DbSet<ProductVariation> ProductVariations => Set<ProductVariation>();
    public DbSet<ProductVariantOption> ProductVariantOptions => Set<ProductVariantOption>();
    public DbSet<ProductVariationSelectedOption> ProductVariationSelectedOptions => Set<ProductVariationSelectedOption>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<BarcodeTemplate> BarcodeTemplates => Set<BarcodeTemplate>();
    public DbSet<BarcodeTemplateField> BarcodeTemplateFields => Set<BarcodeTemplateField>();
    public DbSet<BarcodePrintHistory> BarcodePrintHistories => Set<BarcodePrintHistory>();
    public DbSet<BarcodePrintHistoryItem> BarcodePrintHistoryItems => Set<BarcodePrintHistoryItem>();

    // Suppliers & Purchase
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<Grn> Grns => Set<Grn>();
    public DbSet<GrnItem> GrnItems => Set<GrnItem>();

    // Customers & Sales
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>(); // NEW — split-payment rows
    public DbSet<HeldSale> HeldSales => Set<HeldSale>();           // NEW — parked/held carts

    // Stock Management
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<StockRequisition> StockRequisitions => Set<StockRequisition>();
    public DbSet<StockRequisitionLine> StockRequisitionLines => Set<StockRequisitionLine>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();

    // Accounting
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<Expense> Expenses => Set<Expense>();

    // Stock Ledger  // NEW
    public DbSet<StockLedger> StockLedgers => Set<StockLedger>();

    // Audit (legacy — kept for backward compat during transition)
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Audit (v2)
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AuditEventEntity> AuditEventEntities => Set<AuditEventEntity>();
    public DbSet<AuditEventFieldChange> AuditEventFieldChanges => Set<AuditEventFieldChange>();

    // Pricing Engine
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<OutletPriceOverride> OutletPriceOverrides => Set<OutletPriceOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure PostgreSQL naming convention (snake_case)
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Table names to snake_case
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            // Column names to snake_case
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            // Foreign key names to snake_case
            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var key in entity.GetForeignKeys())
            {
                key.SetConstraintName(ToSnakeCase(key.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }

        // Role Configuration
        modelBuilder.Entity<Business>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.SubscriptionPlan).HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<BusinessFeatureSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FeatureKey).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => new { e.BusinessId, e.FeatureKey }).IsUnique();

            entity.HasOne(e => e.Business)
                .WithMany(b => b.FeatureSettings)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessReminderNotification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReminderType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(500).IsRequired();
            entity.HasIndex(e => new { e.BusinessId, e.ReminderType, e.TargetAt });

            entity.HasOne(e => e.Business)
                .WithMany(b => b.ReminderNotifications)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Role Configuration
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Permissions).HasColumnType("jsonb").IsRequired();
        });

        // User Configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(e => e.InventoryLocationAccessScope)
                .HasMaxLength(40)
                .HasDefaultValue(User.InventoryAccessAssignedOnly)
                .IsRequired();

            entity.HasOne(e => e.Business)
                .WithMany(b => b.Users)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Outlet)
                .WithMany(o => o.Users)
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Outlet Configuration
        modelBuilder.Entity<Outlet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Address).IsRequired();
            entity.Property(e => e.ContactNumber).HasMaxLength(20);

            entity.HasOne(e => e.Business)
                .WithMany(b => b.Outlets)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Manager)
                .WithMany()
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Warehouse Configuration
        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Address).IsRequired();

            entity.HasOne(e => e.Business)
                .WithMany(b => b.Warehouses)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Manager)
                .WithMany()
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Invitation token for onboarding and first-time password setup
        modelBuilder.Entity<UserInvitation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Purpose).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TokenHash).HasMaxLength(128).IsRequired();

            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.Purpose, e.ConsumedAt });

            entity.HasOne(e => e.User)
                .WithMany(u => u.Invitations)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ReplacedByTokenHash).HasMaxLength(128);
            entity.Property(e => e.RevokedReason).HasMaxLength(200);

            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.RevokedAt, e.ExpiresAt });

            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserWarehouseAssignment Configuration
        modelBuilder.Entity<UserWarehouseAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Unique constraint: one warehouse per user
            entity.HasIndex(e => new { e.UserId, e.WarehouseId }).IsUnique();

            // Indexes for efficient querying
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.WarehouseId);
            entity.HasIndex(e => e.BusinessId);

            entity.HasOne(e => e.User)
                .WithMany(u => u.WarehouseAssignments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Warehouse)
                .WithMany()
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserOutletAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.UserId, e.OutletId }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.OutletId);
            entity.HasIndex(e => e.BusinessId);

            entity.HasOne(e => e.User)
                .WithMany(u => u.OutletAssignments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Category Configuration
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);

            entity.HasOne(e => e.ParentCategory)
                .WithMany(c => c.ChildCategories)
                .HasForeignKey(e => e.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Product Configuration
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ProductCode).HasMaxLength(50);
            // Unique partial index: only non-null product codes must be unique.
            // NULL values are intentionally excluded so existing products without a code are unaffected.
            entity.HasIndex(e => e.ProductCode)
                  .IsUnique()
                  .HasFilter("product_code IS NOT NULL");
            entity.Property(e => e.BasePrice).HasPrecision(10, 2);
            entity.Property(e => e.CostPrice).HasPrecision(10, 2);
            entity.Property(e => e.TaxRate).HasPrecision(5, 2);
            entity.HasIndex(e => e.Barcode).IsUnique();
            entity.Property(e => e.Barcode).HasMaxLength(50);

            // Status is stored as smallint; default 1 (Active).
            entity.Property(e => e.Status)
                  .HasConversion<int>()
                  .HasColumnType("smallint")
                  .HasDefaultValue(ProductStatus.Active);

            // IsActive is a computed C# property — not persisted.
            entity.Ignore(e => e.IsActive);

            entity.HasOne(e => e.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ProductVariant Configuration
        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Sku).IsUnique();
            entity.Property(e => e.Sku).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => e.Barcode).IsUnique();
            entity.Property(e => e.Barcode).HasMaxLength(50);
            entity.Property(e => e.Attributes).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.PriceAdjustment).HasPrecision(10, 2);
            entity.Property(e => e.CostAdjustment).HasPrecision(10, 2);

            entity.HasOne(e => e.Product)
                .WithMany(p => p.ProductVariants)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Variation Configuration
        modelBuilder.Entity<Variation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AutoSelectAllOptions)
                .HasColumnName("default_select_all_options")
                .HasDefaultValue(false);
        });

        // VariationOption Configuration
        modelBuilder.Entity<VariationOption>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PriceAdjustment).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.Variation)
                .WithMany(v => v.Options)
                .HasForeignKey(e => e.VariationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProductVariation Configuration
        modelBuilder.Entity<ProductVariation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Product)
                .WithMany(p => p.ProductVariations)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variation)
                .WithMany(v => v.ProductVariations)
                .HasForeignKey(e => e.VariationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProductVariantOption Configuration
        modelBuilder.Entity<ProductVariantOption>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.ProductVariantOptions)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Option)
                .WithMany(o => o.ProductVariantOptions)
                .HasForeignKey(e => e.OptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProductVariationSelectedOption Configuration
        // Stores which specific options are selected for each variation type on a product.
        modelBuilder.Entity<ProductVariationSelectedOption>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Each (product, variation, option) triple must be unique.
            entity.HasIndex(e => new { e.ProductId, e.VariationId, e.OptionId }).IsUnique();

            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variation)
                .WithMany()
                .HasForeignKey(e => e.VariationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Option)
                .WithMany()
                .HasForeignKey(e => e.OptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProductImage Configuration
        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalName).HasMaxLength(260).IsRequired();
            entity.Property(e => e.MimeType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.OriginalPath).HasMaxLength(300).IsRequired();
            entity.Property(e => e.MediumPath).HasMaxLength(300).IsRequired();
            entity.Property(e => e.ThumbPath).HasMaxLength(300).IsRequired();

            entity.HasIndex(e => new { e.ProductId, e.SortOrder });
            // exactly one primary per product — partial unique index on Postgres
            entity.HasIndex(e => e.ProductId)
                .IsUnique()
                .HasFilter("is_primary = true")
                .HasDatabaseName("ux_product_images_one_primary_per_product");

            entity.HasOne(e => e.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BarcodeTemplate Configuration
        modelBuilder.Entity<BarcodeTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
            entity.Property(e => e.TemplateType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.PaperType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.LabelWidthMm).HasPrecision(8, 2);
            entity.Property(e => e.LabelHeightMm).HasPrecision(8, 2);

            entity.HasIndex(e => new { e.BusinessId, e.Name }).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.IsDefault })
                .HasFilter("is_default = true")
                .HasDatabaseName("ux_barcode_templates_one_default_per_business");

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BarcodeTemplateField Configuration
        modelBuilder.Entity<BarcodeTemplateField>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FieldKey).HasMaxLength(60).IsRequired();
            entity.Property(e => e.FontWeight).HasMaxLength(20);
            entity.Property(e => e.Align).HasMaxLength(20);
            entity.Property(e => e.X).HasPrecision(8, 2);
            entity.Property(e => e.Y).HasPrecision(8, 2);
            entity.Property(e => e.Width).HasPrecision(8, 2);
            entity.Property(e => e.Height).HasPrecision(8, 2);
            entity.Property(e => e.FontSize).HasPrecision(8, 2);

            entity.HasIndex(e => new { e.TemplateId, e.FieldKey }).IsUnique();
            entity.HasIndex(e => new { e.TemplateId, e.SortOrder });

            entity.HasOne(e => e.Template)
                .WithMany(t => t.Fields)
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BarcodePrintHistory Configuration
        modelBuilder.Entity<BarcodePrintHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PrintMode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SourceModule).HasMaxLength(60);
            entity.Property(e => e.SourceReferenceType).HasMaxLength(60);
            entity.Property(e => e.LabelWidthMm).HasPrecision(8, 2);
            entity.Property(e => e.LabelHeightMm).HasPrecision(8, 2);

            entity.HasIndex(e => new { e.BusinessId, e.PrintedAt });
            entity.HasIndex(e => new { e.TemplateId, e.PrintedAt });
            entity.HasIndex(e => new { e.PrintedByUserId, e.PrintedAt });
            entity.HasIndex(e => new { e.SourceReferenceType, e.SourceReferenceId });

            entity.HasOne(e => e.Business)
                .WithMany()
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Template)
                .WithMany()
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PrintedByUser)
                .WithMany()
                .HasForeignKey(e => e.PrintedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // BarcodePrintHistoryItem Configuration
        modelBuilder.Entity<BarcodePrintHistoryItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.VariantName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.VariantSku).HasMaxLength(50).IsRequired();
            entity.Property(e => e.BarcodeValue).HasMaxLength(100);
            entity.Property(e => e.SellingPrice).HasPrecision(10, 2);

            entity.HasIndex(e => e.PrintHistoryId);
            entity.HasIndex(e => e.VariantId);

            entity.HasOne(e => e.PrintHistory)
                .WithMany(h => h.Items)
                .HasForeignKey(e => e.PrintHistoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany()
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Inventory Configuration
        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.BatchNumber).HasMaxLength(50);

            // Optimistic concurrency token — maps to PostgreSQL's built-in xmin system column.
            // No DDL column is required; xmin is always present on every PostgreSQL row.
            // ValueGeneratedOnAddOrUpdate tells EF Core NOT to include xmin in INSERT/UPDATE
            // column lists (it's a system column — writing it directly causes a PostgreSQL error).
            // EF Core reads the new value back via RETURNING xmin after each write, then
            // includes it in the WHERE clause of the next UPDATE to detect concurrent changes.
            entity.Property(e => e.XMin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            entity.HasIndex(e => new { e.VariantId, e.LocationId, e.LocationType }).IsUnique();

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.Inventories)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Supplier Configuration
        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Contact).HasMaxLength(255);
            entity.Property(e => e.CreditLimit).HasPrecision(10, 2);
        });

        // PurchaseOrder Configuration
        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PoNumber).HasMaxLength(30).IsRequired();
            entity.HasIndex(e => e.PoNumber).IsUnique();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(64);
            entity.HasIndex(e => new { e.WarehouseId, e.IdempotencyKey })
                .IsUnique()
                .HasFilter("idempotency_key IS NOT NULL");
            entity.Property(e => e.TotalAmount).HasPrecision(10, 2);
            // status values: draft | pending | sent_back | approved | partially_received | fully_received | completed | cancelled | rejected
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);

            entity.HasOne(e => e.Supplier)
                .WithMany(s => s.PurchaseOrders)
                .HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Warehouse)
                .WithMany(w => w.PurchaseOrders)
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Creator)
                .WithMany(u => u.PurchaseOrders)
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // PurchaseOrderItem Configuration
        modelBuilder.Entity<PurchaseOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPrice).HasPrecision(10, 2);
            entity.Property(e => e.Discount).HasPrecision(5, 2);
            entity.Property(e => e.Tax).HasPrecision(5, 2);
            entity.Property(e => e.Unit).HasMaxLength(30);

            entity.HasOne(e => e.PurchaseOrder)
                .WithMany(po => po.Items)
                .HasForeignKey(e => e.PoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.PurchaseOrderItems)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Grn Configuration
        modelBuilder.Entity<Grn>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);    // NEW

            entity.HasOne(e => e.PurchaseOrder)
                .WithMany(po => po.Grns)
                .HasForeignKey(e => e.PoId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Creator)
                .WithMany(u => u.Grns)
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // GrnItem Configuration
        modelBuilder.Entity<GrnItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitCost).HasPrecision(10, 2);  // NEW
            entity.Property(e => e.Notes).HasMaxLength(500);        // NEW

            entity.HasOne(e => e.Grn)
                .WithMany(g => g.Items)
                .HasForeignKey(e => e.GrnId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PurchaseOrderItem)
                .WithMany(poi => poi.GrnItems)
                .HasForeignKey(e => e.PoItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Customer Configuration
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(255);
        });

        // Sale Configuration
        modelBuilder.Entity<Sale>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SaleNumber).HasMaxLength(30).IsRequired();    // UPDATED
            entity.Property(e => e.TotalAmount).HasPrecision(10, 2);
            entity.Property(e => e.Discount).HasPrecision(10, 2);
            entity.Property(e => e.Tax).HasPrecision(10, 2);
            entity.Property(e => e.PaymentMethod).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(64);             // UPDATED

            entity.HasIndex(e => e.SaleDate);
            entity.HasIndex(e => e.OutletId);
            // Idempotency: unique per outlet to prevent double-submission
            entity.HasIndex(e => new { e.OutletId, e.IdempotencyKey })
                  .IsUnique()
                  .HasFilter("idempotency_key IS NOT NULL");                      // UPDATED

            entity.HasOne(e => e.Outlet)
                .WithMany(o => o.Sales)
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Customer)
                .WithMany(c => c.Sales)
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Cashier)
                .WithMany(u => u.Sales)
                .HasForeignKey(e => e.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // SaleItem Configuration
        modelBuilder.Entity<SaleItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPrice).HasPrecision(10, 2);
            entity.Property(e => e.Subtotal).HasPrecision(10, 2);
            entity.Property(e => e.DiscountAmount).HasPrecision(10, 2);
            entity.Property(e => e.AppliedRuleName).HasMaxLength(200);           // UPDATED

            entity.HasOne(e => e.Sale)
                .WithMany(s => s.Items)
                .HasForeignKey(e => e.SaleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.SaleItems)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // NEW — SalePayment Configuration
        modelBuilder.Entity<SalePayment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Method).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.Tendered).HasPrecision(10, 2);

            entity.HasIndex(e => e.SaleId);

            entity.HasOne(e => e.Sale)
                .WithMany(s => s.Payments)
                .HasForeignKey(e => e.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // NEW — HeldSale Configuration
        modelBuilder.Entity<HeldSale>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ItemsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.DiscountPercent).HasPrecision(5, 2);
            entity.Property(e => e.PaymentMethod).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Note).HasMaxLength(300);

            entity.HasIndex(e => e.OutletId);
            entity.HasIndex(e => e.CashierId);
            entity.HasIndex(e => e.HeldAt);

            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Cashier)
                .WithMany()
                .HasForeignKey(e => e.CashierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Customer)
                .WithMany()
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // StockTransfer Configuration
        modelBuilder.Entity<StockTransfer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransferNo).HasMaxLength(40);
            entity.Property(e => e.TransferType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.FromLocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ToLocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);

            entity.HasIndex(e => e.TransferNo)
                .IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.TransferDate);
            entity.HasIndex(e => new { e.FromLocationType, e.FromLocationId });
            entity.HasIndex(e => new { e.ToLocationType, e.ToLocationId });
            entity.HasIndex(e => e.RelatedRequisitionId);

            entity.HasOne(e => e.Approver)
                .WithMany(u => u.StockTransfersApproved)
                .HasForeignKey(e => e.ApprovedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Submitter)
                .WithMany()
                .HasForeignKey(e => e.SubmittedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Dispatcher)
                .WithMany()
                .HasForeignKey(e => e.DispatchedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Receiver)
                .WithMany()
                .HasForeignKey(e => e.ReceivedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Rejector)
                .WithMany()
                .HasForeignKey(e => e.RejectedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Canceller)
                .WithMany()
                .HasForeignKey(e => e.CancelledBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Updater)
                .WithMany()
                .HasForeignKey(e => e.UpdatedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Creator)
                .WithMany(u => u.StockTransfersCreated)
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RelatedRequisition)
                .WithMany(r => r.Transfers)
                .HasForeignKey(e => e.RelatedRequisitionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // StockTransferItem Configuration
        modelBuilder.Entity<StockTransferItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitCost).HasPrecision(18, 4);
            entity.Property(e => e.Remarks).HasMaxLength(1000);

            entity.HasOne(e => e.Transfer)
                .WithMany(t => t.Items)
                .HasForeignKey(e => e.TransferId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.StockTransferItems)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StockRequisition Configuration
        modelBuilder.Entity<StockRequisition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequisitionNo).HasMaxLength(40).IsRequired();
            entity.Property(e => e.RequestingLocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SourceLocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);

            entity.HasIndex(e => e.RequisitionNo)
                .IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RequestDate);
            entity.HasIndex(e => new { e.RequestingLocationType, e.RequestingLocationId });
            entity.HasIndex(e => new { e.SourceLocationType, e.SourceLocationId });

            entity.HasOne(e => e.Requester)
                .WithMany()
                .HasForeignKey(e => e.RequestedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Submitter)
                .WithMany()
                .HasForeignKey(e => e.SubmittedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Approver)
                .WithMany()
                .HasForeignKey(e => e.ApprovedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Rejector)
                .WithMany()
                .HasForeignKey(e => e.RejectedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Closer)
                .WithMany()
                .HasForeignKey(e => e.ClosedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Updater)
                .WithMany()
                .HasForeignKey(e => e.UpdatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StockRequisitionLine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Remarks).HasMaxLength(1000);

            entity.HasIndex(e => e.RequisitionId);
            entity.HasIndex(e => e.VariantId);

            entity.HasOne(e => e.Requisition)
                .WithMany(r => r.Lines)
                .HasForeignKey(e => e.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany()
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StockAdjustment Configuration
        modelBuilder.Entity<StockAdjustment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AdjustmentNumber).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);

            entity.HasIndex(e => e.AdjustmentNumber)
                .IsUnique();

            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.Adjuster)
                .WithMany(u => u.StockAdjustments)
                .HasForeignKey(e => e.AdjustedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Approver)
                .WithMany(u => u.StockAdjustmentsApproved)
                .HasForeignKey(e => e.ApprovedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Rejector)
                .WithMany(u => u.StockAdjustmentsRejected)
                .HasForeignKey(e => e.RejectedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Canceller)
                .WithMany(u => u.StockAdjustmentsCancelled)
                .HasForeignKey(e => e.CancelledBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Lines)
                .WithOne(l => l.StockAdjustment)
                .HasForeignKey(l => l.StockAdjustmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockAdjustmentLine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reason).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(2000);

            entity.HasIndex(e => e.StockAdjustmentId);
            entity.HasIndex(e => e.VariantId);

            entity.HasOne(e => e.Variant)
                .WithMany(v => v.StockAdjustmentLines)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StockCount Configuration
        modelBuilder.Entity<StockCount>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.StockCountNo).HasMaxLength(40).IsRequired();
            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);
            entity.Property(e => e.Remarks).HasMaxLength(1000);

            entity.HasIndex(e => e.StockCountNo).IsUnique();
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.LocationId);
            entity.HasIndex(e => e.StockCountDate);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.BusinessId, e.LocationType, e.LocationId, e.CreatedAt });

            entity.HasIndex(e => new { e.BusinessId, e.LocationType, e.LocationId })
                .IsUnique()
                .HasFilter("status in ('Draft','Submitted')");

            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Submitter)
                .WithMany()
                .HasForeignKey(e => e.SubmittedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Approver)
                .WithMany()
                .HasForeignKey(e => e.ApprovedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Rejector)
                .WithMany()
                .HasForeignKey(e => e.RejectedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Lines)
                .WithOne(l => l.StockCount)
                .HasForeignKey(l => l.StockCountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockCountLine>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ProductName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ProductCode).HasMaxLength(100).IsRequired();
            entity.Property(e => e.VariantName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CurrentStock).HasPrecision(18, 3).IsRequired();
            entity.Property(e => e.PhysicalStock).HasPrecision(18, 3);
            entity.Property(e => e.Difference).HasPrecision(18, 3);
            entity.Property(e => e.Remarks).HasMaxLength(1000);

            entity.HasIndex(e => e.StockCountId);
            entity.HasIndex(e => e.VariantId);
            entity.HasIndex(e => new { e.StockCountId, e.VariantId }).IsUnique();

            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Variant)
                .WithMany()
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Account Configuration
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Balance).HasPrecision(10, 2);

            entity.HasOne(e => e.Business)
                .WithMany(b => b.Accounts)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Transaction Configuration
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.Type).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.ReferenceType).HasMaxLength(50);

            entity.HasOne(e => e.Account)
                .WithMany(a => a.Transactions)
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Bill Configuration
        modelBuilder.Entity<Bill>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AmountDue).HasPrecision(10, 2);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();

            entity.HasOne(e => e.Supplier)
                .WithMany(s => s.Bills)
                .HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PurchaseOrder)
                .WithMany(po => po.Bills)
                .HasForeignKey(e => e.PoId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Expense Configuration
        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Category).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(10, 2);

            entity.HasOne(e => e.Outlet)
                .WithMany(o => o.Expenses)
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AuditLog Configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Module).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Details).HasColumnType("jsonb");
            entity.Property(e => e.IpAddress).HasMaxLength(45);

            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AuditEvent Configuration
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActionType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.ActionSummary).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Module).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PrimaryEntityType).HasMaxLength(80);
            entity.Property(e => e.PrimaryEntityId).HasMaxLength(80);
            entity.Property(e => e.RequestMethod).HasMaxLength(10);
            entity.Property(e => e.RequestPath).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.DeviceName).HasMaxLength(120);
            entity.Property(e => e.Browser).HasMaxLength(60);
            entity.Property(e => e.Os).HasMaxLength(60);
            entity.Property(e => e.Status).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Metadata).HasColumnType("jsonb");

            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => new { e.RealUserId, e.CreatedAt });
            entity.HasIndex(e => new { e.Module, e.ActionType, e.CreatedAt });
            entity.HasIndex(e => new { e.PrimaryEntityType, e.PrimaryEntityId });
            entity.HasIndex(e => new { e.OutletId, e.CreatedAt });
        });

        // AuditEventEntity Configuration
        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.EntityId).HasMaxLength(80).IsRequired();
            entity.Property(e => e.OperationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Metadata).HasColumnType("jsonb");
            entity.Property(e => e.IsInternalOperation).HasDefaultValue(false);
            entity.Property(e => e.InternalOperationName).HasMaxLength(120);

            entity.HasIndex(e => e.AuditEventId);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });

            entity.HasOne(e => e.AuditEvent)
                .WithMany(a => a.Entities)
                .HasForeignKey(e => e.AuditEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuditEventFieldChange Configuration
        modelBuilder.Entity<AuditEventFieldChange>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FieldName).HasMaxLength(120).IsRequired();
            entity.Property(e => e.OldValue).HasColumnType("jsonb");
            entity.Property(e => e.NewValue).HasColumnType("jsonb");
            entity.Property(e => e.OldDisplayValue).HasMaxLength(500);
            entity.Property(e => e.NewDisplayValue).HasMaxLength(500);
            entity.Property(e => e.ReferenceEntityType).HasMaxLength(80);
            entity.Property(e => e.IsReferenceField).HasDefaultValue(false);

            entity.HasIndex(e => e.AuditEventEntityId);

            entity.HasOne(e => e.AuditEventEntity)
                .WithMany(a => a.FieldChanges)
                .HasForeignKey(e => e.AuditEventEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PriceRule Configuration
        modelBuilder.Entity<PriceRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.RuleType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.DiscountType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.DiscountValue).HasPrecision(10, 2);
            // Hot-path index: the matching query filters on IsActive + RuleType + TargetId
            entity.HasIndex(e => new { e.IsActive, e.RuleType, e.TargetId });
            entity.HasIndex(e => new { e.ValidFrom, e.ValidTo });
        });

        // OutletPriceOverride Configuration
        modelBuilder.Entity<OutletPriceOverride>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OverrideType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.OverrideValue).HasPrecision(10, 2);
            // One *active* override per outlet × variant; inactive rows are excluded so
            // a new override can be created after the old one is deactivated.
            entity.HasIndex(e => new { e.OutletId, e.ProductVariantId })
                  .IsUnique()
                  .HasFilter("is_active = true");
            entity.HasOne(e => e.Outlet)
                .WithMany()
                .HasForeignKey(e => e.OutletId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ProductVariant)
                .WithMany()
                .HasForeignKey(e => e.ProductVariantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================================
        // NEW — StockLedger Configuration
        // =====================================================================
        modelBuilder.Entity<StockLedger>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.LocationType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.TransactionType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ReferenceType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Remarks).HasMaxLength(500);

            // Core query paths: look up the full timeline for a variant+location,
            // or jump straight to all rows belonging to one source document.
            entity.HasIndex(e => new { e.VariantId, e.LocationId, e.LocationType, e.CreatedAt });
            entity.HasIndex(e => new { e.ReferenceType, e.ReferenceId });
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Variant)
                .WithMany()
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAuditImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAuditImmutability()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditEvent or AuditEventEntity or AuditEventFieldChange)
            {
                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    throw new InvalidOperationException(
                        $"Audit records are immutable; cannot {entry.State} {entry.Entity.GetType().Name}.");
                }
            }
        }
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new System.Text.StringBuilder();
        result.Append(char.ToLowerInvariant(input[0]));

        for (int i = 1; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsUpper(c))
            {
                result.Append('_');
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
