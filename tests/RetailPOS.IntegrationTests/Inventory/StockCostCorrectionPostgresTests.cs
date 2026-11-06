using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Infrastructure.StockCounts;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.OwnerDashboards;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingReports;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.InventoryReports;
using RetailPOS.Infrastructure.OwnerDashboards;
using RetailPOS.Infrastructure.ReadStores;
using RetailPOS.Infrastructure.StockTransfers;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

/// <summary>
/// The approved correction of uncosted receipts, on real PostgreSQL with the data shape the old code left behind: an
/// uncosted GRN row, reliable-looking zero transfers, a later sale, and stock with no ledger at all.
/// </summary>
public sealed class StockCostCorrectionPostgresTests
{
    private const long Business = 1;
    private const long OtherBusiness = 2;
    private const long Actor = 101;
    private const long OtherActor = 102;
    private const long Warehouse = 21;
    private const long Outlet = 11;
    private const long Variant = 301;
    private const long SeededVariant = 302;
    private const long Grn = 40;
    private const long Transfer = 601;
    private const long InventoryAccount = 900;

    [Fact]
    public async Task Preview_WritesNothing_AndShowsTheCorrectionsAndTheirEffect() =>
        await RunAsync(async database =>
        {
            await using var context = CreateContext(database);
            var before = await context.StockLedgers.CountAsync();

            var preview = await Service(context).PreviewAsync(Business, CancellationToken.None);

            Assert.Equal(before, await context.StockLedgers.CountAsync());
            Assert.Equal(
                [(1000m, "grn"), (200m, "transfer_out"), (200m, "transfer_in")],
                preview.Plan.Corrections.Select(item => (item.Value, item.Movement.TransactionType)));
            Assert.Equal(0m, preview.Before.Sum(row => row.Value));
            // Warehouse 80 left at 10, outlet 20 received less 5 sold.
            Assert.Equal(800m + 150m, preview.After.Sum(row => row.Value));
            Assert.Equal(950m, preview.InventoryGlBalance);
        });

    [Fact]
    public async Task Apply_OnlyAppends_AndReconcilesTheAcceptedReportToTheGl() =>
        await RunAsync(async database =>
        {
            List<StockLedger> originals;
            await using (var context = CreateContext(database))
            {
                originals = await context.StockLedgers.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
                var journalsBefore = await context.JournalEntries.CountAsync();
                var stockBefore = await context.Inventories.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Quantity).ToListAsync();

                Assert.Equal(3, await Service(context).ApplyAsync(Business, Actor, 3, CancellationToken.None));

                Assert.Equal(journalsBefore, await context.JournalEntries.CountAsync());
                Assert.Equal(stockBefore, await context.Inventories.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Quantity).ToListAsync());
            }

            await using var verify = CreateContext(database);
            var rows = await verify.StockLedgers.AsNoTracking().OrderBy(row => row.Id).ToListAsync();

            // Every original row is exactly as it was.
            foreach (var original in originals)
            {
                var now = rows.Single(row => row.Id == original.Id);
                Assert.Equal(
                    (original.UnitCost, original.MovementValue, original.IsCostValuationReliable, original.QtyIn, original.QtyOut, original.BalanceAfter),
                    (now.UnitCost, now.MovementValue, now.IsCostValuationReliable, now.QtyIn, now.QtyOut, now.BalanceAfter));
            }

            var corrections = rows.Where(row => row.ReferenceType == StockLedgerReferenceType.StockCostCorrection).ToList();
            Assert.Equal(3, corrections.Count);
            Assert.All(corrections, row =>
            {
                Assert.Equal(StockLedgerTransactionType.CostCorrection, row.TransactionType);
                Assert.Equal((0, 0), (row.QtyIn, row.QtyOut));
                Assert.Equal(Actor, row.CreatedBy);
            });

            var report = await new InventoryValuationReadStore(verify, TestCompanySettings.Provider()).GetAsync(
                new InventoryValuationFilter(DateTime.UtcNow.Date.AddDays(2), null, null, null, null, null, null),
                Business,
                CancellationToken.None);
            Assert.Empty(report.UnvaluedMovements);
            Assert.Equal(950m, report.TotalInventoryValue);
            Assert.Equal(950m, report.GeneralLedgerBalance);
            Assert.Equal(0m, report.ReconciliationDifference);

            var audit = await verify.AuditEvents.Include(item => item.Entities).SingleAsync();
            Assert.Equal(Business, audit.BusinessId);
            Assert.Equal(Actor, audit.RealUserId);
            Assert.Equal(3, audit.Entities.Count);
        });

    [Fact]
    public async Task ReportAsOfAnEarlierDate_IsNotRestated() =>
        await RunAsync(async database =>
        {
            await using var context = CreateContext(database);
            await Service(context).ApplyAsync(Business, Actor, 3, CancellationToken.None);

            var report = await new InventoryValuationReadStore(context, TestCompanySettings.Provider()).GetAsync(
                new InventoryValuationFilter(new DateTime(2026, 3, 31), null, null, null, null, null, null),
                Business,
                CancellationToken.None);

            Assert.Single(report.UnvaluedMovements);
        });

    [Fact]
    public async Task Dashboard_ValuesCorrectedStock_AndLeavesStockWithoutEvidenceNotValued() =>
        await RunAsync(async database =>
        {
            await using var context = CreateContext(database);
            await Service(context).ApplyAsync(Business, Actor, 3, CancellationToken.None);

            var snapshot = await new InventoryDashboardReadStore(context, TimeProvider.System).GetSnapshotAsync(
                new OwnerDashboardScope(
                    Business,
                    "Asia/Dhaka",
                    new DateOnly(2026, 11, 7),
                    [new OwnerDashboardLocation(Outlet, "Outlet", OwnerDashboardLocationTypes.Outlet)],
                    [new OwnerDashboardLocation(Warehouse, "Warehouse", OwnerDashboardLocationTypes.Warehouse)],
                    [],
                    null,
                    false,
                    false),
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 11, 7),
                true,
                CancellationToken.None);

            Assert.Equal(800m, snapshot.Facts.Single(fact => fact.VariantId == Variant && fact.LocationId == Warehouse).StockValue);
            Assert.Equal(150m, snapshot.Facts.Single(fact => fact.VariantId == Variant && fact.LocationId == Outlet).StockValue);
            Assert.Null(snapshot.Facts.Single(fact => fact.VariantId == SeededVariant).StockValue);
        });

    [Fact]
    public async Task ApplyingAgain_FindsNothingToDo() =>
        await RunAsync(async database =>
        {
            await using (var context = CreateContext(database))
                await Service(context).ApplyAsync(Business, Actor, 3, CancellationToken.None);

            await using var again = CreateContext(database);
            var preview = await Service(again).PreviewAsync(Business, CancellationToken.None);

            Assert.Empty(preview.Plan.Corrections);
            Assert.Equal(950m, preview.Before.Sum(row => row.Value));
            Assert.Equal(0, await Service(again).ApplyAsync(Business, Actor, 0, CancellationToken.None));
            Assert.Equal(3, await again.StockLedgers.CountAsync(row => row.ReferenceType == StockLedgerReferenceType.StockCostCorrection));
        });

    [Fact]
    public async Task TwoAppliesAtOnce_WriteOneSetOfCorrections() =>
        await RunAsync(async database =>
        {
            await using var first = CreateContext(database);
            await using var second = CreateContext(database);

            var outcomes = await Task.WhenAll(
                AttemptAsync(() => Service(first).ApplyAsync(Business, Actor, 3, CancellationToken.None)),
                AttemptAsync(() => Service(second).ApplyAsync(Business, Actor, 3, CancellationToken.None)));

            Assert.Single(outcomes, outcome => outcome is null);
            await using var verify = CreateContext(database);
            var corrected = await verify.StockLedgers
                .Where(row => row.ReferenceType == StockLedgerReferenceType.StockCostCorrection)
                .Select(row => row.ReferenceId)
                .ToListAsync();
            Assert.Equal(3, corrected.Count);
            Assert.Equal(3, corrected.Distinct().Count());
            Assert.Single(await verify.AuditEvents.ToListAsync());
        });

    [Fact]
    public async Task StockCount_DoesNotReportACorrectionAsMovement_ButStillReportsRealMovement() =>
        await RunAsync(async database =>
        {
            const long stockCountId = 950;
            await using (var seed = CreateContext(database))
            {
                seed.StockCounts.Add(new StockCount
                {
                    Id = stockCountId,
                    StockCountNo = "SC-CORR-1",
                    BusinessId = Business,
                    LocationId = Warehouse,
                    LocationType = "warehouse",
                    StockCountDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    CreatedBy = Actor,
                    CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    Lines = [new StockCountLine { ProductId = 300, VariantId = Variant, ProductName = "Product", ProductCode = "CORR-1", VariantName = "A", CurrentStock = 80 }]
                });
                await seed.SaveChangesAsync();
                await Service(seed).ApplyAsync(Business, Actor, 3, CancellationToken.None);
            }

            await using (var context = CreateContext(database))
            {
                var afterCorrection = await new StockCountSearchReadStore(context, Mock.Of<IStockCountAsyncAdjustmentSwitch>())
                    .GetByIdAsync(stockCountId, CancellationToken.None);
                Assert.False(afterCorrection!.HasPostGenerationMovements);
                Assert.Equal(0, afterCorrection.PostGenerationMovementCount);
                Assert.Null(afterCorrection.LastPostGenerationMovementAt);

                var moved = Row(Variant, Warehouse, "warehouse", StockLedgerTransactionType.Adjustment, StockLedgerReferenceType.StockAdjustment, 77, 0, 1, 79, 10m, true, new DateTime(2026, 7, 1), 99);
                moved.CreatedAt = DateTime.UtcNow.AddMinutes(1);
                context.StockLedgers.Add(moved);
                await context.SaveChangesAsync();

                var afterMovement = await new StockCountSearchReadStore(context, Mock.Of<IStockCountAsyncAdjustmentSwitch>())
                    .GetByIdAsync(stockCountId, CancellationToken.None);
                Assert.True(afterMovement!.HasPostGenerationMovements);
                Assert.Equal(1, afterMovement.PostGenerationMovementCount);
            }
        });

    [Fact]
    public async Task MovementReport_DoesNotListAProductWhoseOnlyEntryIsACorrection() =>
        await RunAsync(async database =>
        {
            await using var context = CreateContext(database);
            await Service(context).ApplyAsync(Business, Actor, 3, CancellationToken.None);

            var today = await new StockMovementReportReadStore(context).GetAsync(
                new InventoryReportReadScope(Business, [Outlet], [Warehouse]),
                new StockMovementReportFilter(null, null, null, null, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), "productName", "asc", 1, 25),
                CancellationToken.None);
            Assert.Empty(today.Items);
            Assert.Equal(0, today.TotalCount);

            // Over the whole history the quantities are the real ones, and the opening balance is untouched.
            var history = await new StockMovementReportReadStore(context).GetAsync(
                new InventoryReportReadScope(Business, [Outlet], [Warehouse]),
                new StockMovementReportFilter(null, Warehouse, null, null, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddHours(1), "productName", "asc", 1, 25),
                CancellationToken.None);
            var row = Assert.Single(history.Items);
            Assert.Equal((100, 20), (row.StockIn, row.StockOut));
        });

    [Fact]
    public async Task TransactionReport_ShowsTheCorrection_WithNoEffectOnQuantities() =>
        await RunAsync(async database =>
        {
            await using var context = CreateContext(database);
            var scope = new InventoryReportReadScope(Business, [Outlet], [Warehouse]);
            var filter = new StockTransactionReportFilter(300, Variant, Warehouse, "warehouse", null, null);
            var before = await new StockTransactionReportReadStore(context).GetAsync(scope, filter, CancellationToken.None);

            await Service(context).ApplyAsync(Business, Actor, 3, CancellationToken.None);
            var after = await new StockTransactionReportReadStore(context).GetAsync(scope, filter, CancellationToken.None);

            Assert.Equal(before.Summary, after.Summary);
            Assert.Equal(before.Rows.Count + 2, after.Rows.Count);
            Assert.All(after.Rows.Skip(before.Rows.Count), row => Assert.Equal((0, 0), (row.QuantityIn, row.QuantityOut)));
        });

    [Fact]
    public async Task ACountThatDiffersFromThePreview_WritesNothing() =>
        await RunAsync(async database =>
        {
            await using var context = CreateContext(database);
            var before = await context.StockLedgers.CountAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Service(context).ApplyAsync(Business, Actor, 2, CancellationToken.None));

            await using var verify = CreateContext(database);
            Assert.Equal(before, await verify.StockLedgers.CountAsync());
            Assert.Empty(await verify.AuditEvents.ToListAsync());
        });

    [Fact]
    public async Task AnActorFromAnotherBusiness_IsRefused() =>
        await RunAsync(async database =>
        {
            await using var context = CreateContext(database);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Service(context).ApplyAsync(Business, OtherActor, 3, CancellationToken.None));

            Assert.Empty(await context.StockLedgers.Where(row => row.ReferenceType == StockLedgerReferenceType.StockCostCorrection).ToListAsync());
        });

    [Fact]
    public async Task AGrnThatDisagreesWithItsJournal_IsLeftAlone_WithEverythingThatDependsOnIt() =>
        await RunAsync(async database =>
        {
            await using (var seed = CreateContext(database))
            {
                var line = await seed.JournalEntryLines.SingleAsync(item => item.AccountId == InventoryAccount && item.DebitAmount > 0m);
                line.DebitAmount = 900m;
                await seed.SaveChangesAsync();
            }

            await using var context = CreateContext(database);
            var preview = await Service(context).PreviewAsync(Business, CancellationToken.None);

            Assert.Empty(preview.Plan.Corrections);
            Assert.Contains(preview.Plan.Skipped, item => item.Reason.Contains("journal"));
            Assert.Contains(preview.Plan.Skipped, item => item.Reason.Contains("explain"));
        });

    [Fact]
    public async Task AnotherBusiness_IsNeverTouched() =>
        await RunAsync(async database =>
        {
            await using var context = CreateContext(database);

            var preview = await Service(context).PreviewAsync(OtherBusiness, CancellationToken.None);

            Assert.Empty(preview.Plan.Corrections);
            Assert.Equal(0, await Service(context).ApplyAsync(OtherBusiness, OtherActor, 0, CancellationToken.None));
            Assert.Empty(await context.StockLedgers.Where(row => row.ReferenceType == StockLedgerReferenceType.StockCostCorrection).ToListAsync());
        });

    [Fact]
    public async Task NewDispatchAfterACorrection_CarriesTheCorrectedCost() =>
        await RunAsync(async database =>
        {
            await using (var context = CreateContext(database))
            {
                await Service(context).ApplyAsync(Business, Actor, 3, CancellationToken.None);
                context.StockTransfers.Add(new StockTransfer
                {
                    Id = 602,
                    TransferNo = "TRF-CORR-602",
                    TransferType = StockTransfer.TransferTypeDirect,
                    FromLocationId = Warehouse,
                    FromLocationType = "warehouse",
                    ToLocationId = Outlet,
                    ToLocationType = "outlet",
                    TransferDate = DateTime.UtcNow,
                    Status = StockTransfer.StatusDraft,
                    CreatedBy = Actor,
                    Items = [new StockTransferItem { VariantId = Variant, Quantity = 10, RequestedQuantity = 10, TransferQuantity = 10 }]
                });
                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(database))
            {
                await new StockTransferDispatchStore(
                        context,
                        new StockLedgerWriter(context, NullLogger<StockLedgerWriter>.Instance),
                        NullLogger<StockTransferDispatchStore>.Instance)
                    .SubmitAndDispatchAsync(602, Business, Actor, autoApprove: true, CancellationToken.None);
            }

            await using var verify = CreateContext(database);
            var dispatched = await verify.StockLedgers.SingleAsync(row =>
                row.ReferenceType == StockLedgerReferenceType.StockTransfer && row.ReferenceId == 602 && row.QtyOut > 0);
            Assert.True(dispatched.IsCostValuationReliable);
            Assert.Equal(10m, dispatched.UnitCost);
            Assert.Equal(100m, dispatched.MovementValue);
        });

    [Fact]
    public async Task TransferInTransit_WhenCorrected_IsReceivedAtTheCorrectedValue() =>
        await RunAsync(async database =>
        {
            await using (var seed = CreateContext(database))
            {
                // Dispatched by the old code, still on the road when the correction runs.
                seed.StockTransfers.Add(new StockTransfer
                {
                    Id = 603,
                    TransferNo = "TRF-CORR-603",
                    TransferType = StockTransfer.TransferTypeDirect,
                    FromLocationId = Warehouse,
                    FromLocationType = "warehouse",
                    ToLocationId = Outlet,
                    ToLocationType = "outlet",
                    TransferDate = DateTime.UtcNow,
                    Status = StockTransfer.StatusInTransit,
                    CreatedBy = Actor,
                    Items = [new StockTransferItem { VariantId = Variant, Quantity = 5, RequestedQuantity = 5, TransferQuantity = 5 }]
                });
                seed.StockLedgers.Add(Row(Variant, Warehouse, "warehouse", StockLedgerTransactionType.TransferOut, StockLedgerReferenceType.StockTransfer, 603, 0, 5, 75, 0m, true, new DateTime(2026, 3, 3), 3));
                var stock = await seed.Inventories.SingleAsync(row => row.VariantId == Variant && row.LocationId == Warehouse);
                stock.Quantity = 75;
                await seed.SaveChangesAsync();
            }

            await using (var context = CreateContext(database))
            {
                var preview = await Service(context).PreviewAsync(Business, CancellationToken.None);
                await Service(context).ApplyAsync(Business, Actor, preview.Plan.Corrections.Count, CancellationToken.None);
            }

            await using (var context = CreateContext(database))
            {
                await new StockTransferReceiveStore(
                        context,
                        new StockLedgerWriter(context, NullLogger<StockLedgerWriter>.Instance),
                        Mock.Of<IStockTransferReceiptAuditWriter>(),
                        NullLogger<StockTransferReceiveStore>.Instance)
                    .ReceiveAsync(603, Business, null, Actor, CancellationToken.None);
            }

            await using var verify = CreateContext(database);
            var received = await verify.StockLedgers.SingleAsync(row =>
                row.ReferenceType == StockLedgerReferenceType.StockTransfer && row.ReferenceId == 603 && row.QtyIn > 0);
            Assert.True(received.IsCostValuationReliable);
            Assert.Equal(50m, received.MovementValue);
        });

    private static async Task<Exception?> AttemptAsync(Func<Task<int>> apply)
    {
        try
        {
            await apply();
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException)
        {
            return exception;
        }
    }

    private static StockCostCorrectionService Service(RetailPOSDbContext context) =>
        new(context, new StockLedgerWriter(context, NullLogger<StockLedgerWriter>.Instance));

    private static StockLedger Row(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        string referenceType,
        long referenceId,
        int qtyIn,
        int qtyOut,
        int balance,
        decimal unitCost,
        bool reliable,
        DateTime date,
        long sequence) => new()
    {
        VariantId = variantId,
        LocationId = locationId,
        LocationType = locationType,
        TransactionType = transactionType,
        QtyIn = qtyIn,
        QtyOut = qtyOut,
        BalanceAfter = balance,
        UnitCost = unitCost,
        MovementValue = (qtyIn + qtyOut) * unitCost,
        IsCostValuationReliable = reliable,
        BusinessDate = date,
        MovementSequence = sequence,
        ReferenceType = referenceType,
        ReferenceId = referenceId,
        CreatedAt = DateTime.SpecifyKind(date, DateTimeKind.Utc)
    };

    private static Task RunAsync(Func<PostgresTestDatabase, Task> scenario) =>
        PostgresTestDatabase.RunAsync(
            Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
                ?? throw new InvalidOperationException("RETAILPOS_TEST_POSTGRES must target the Test stack."),
            PostgresTestDatabasePrefix.StockTransferDispatch,
            async database =>
            {
                await using (var seed = CreateContext(database))
                {
                    await seed.Database.MigrateAsync();
                    await SeedAsync(seed);
                }

                await scenario(database);
            });

    private static RetailPOSDbContext CreateContext(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    /// <summary>
    /// 100 received at 10 (journal 1,000), 20 moved to the outlet at a claimed zero, 5 sold there at 10 (journal 50 out).
    /// Master cost is 4,000 on purpose: if it leaked into a figure, these totals would not hold.
    /// </summary>
    private static async Task SeedAsync(RetailPOSDbContext context)
    {
        context.Businesses.AddRange(
            new Business { Id = Business, Name = "Correction business", IsActive = true },
            new Business { Id = OtherBusiness, Name = "Other business", IsActive = true });
        context.Users.AddRange(
            new User { Id = Actor, BusinessId = Business, Name = "Owner", Email = "owner@example.test", PasswordHash = "test-only", IsActive = true },
            new User { Id = OtherActor, BusinessId = OtherBusiness, Name = "Other", Email = "other@example.test", PasswordHash = "test-only", IsActive = true });
        context.Categories.Add(new Category { Id = 201, BusinessId = Business, Name = "Correction" });
        context.Outlets.Add(new Outlet { Id = Outlet, BusinessId = Business, Name = "Outlet", Address = "A" });
        context.Warehouses.Add(new Warehouse { Id = Warehouse, BusinessId = Business, Name = "Warehouse", Address = "B" });
        context.Products.Add(new Product { Id = 300, BusinessId = Business, Name = "Product", ProductCode = "CORR-1", CategoryId = 201, CostPrice = 4000m, BasePrice = 5000m, Status = ProductStatus.Active });
        context.ProductVariants.AddRange(
            new ProductVariant { Id = Variant, BusinessId = Business, ProductId = 300, Name = "A", Sku = "CORR-A", Attributes = "{}" },
            new ProductVariant { Id = SeededVariant, BusinessId = Business, ProductId = 300, Name = "B", Sku = "CORR-B", Attributes = "{}" });
        context.Inventories.AddRange(
            new InventoryEntity { VariantId = Variant, LocationId = Warehouse, LocationType = "warehouse", Quantity = 80 },
            new InventoryEntity { VariantId = Variant, LocationId = Outlet, LocationType = "outlet", Quantity = 15 },
            new InventoryEntity { VariantId = SeededVariant, LocationId = Warehouse, LocationType = "warehouse", Quantity = 500 });

        context.Suppliers.Add(new Supplier { Id = 701, BusinessId = Business, Name = "Supplier" });
        context.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 702,
            PoNumber = "PO-CORR-1",
            SupplierId = 701,
            WarehouseId = Warehouse,
            OrderDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = "completed",
            Items = [new PurchaseOrderItem { Id = 703, VariantId = Variant, Quantity = 100, UnitPrice = 10m }]
        });
        context.Grns.Add(new RetailPOS.Core.Entities.Grn
        {
            Id = Grn,
            PoId = 702,
            ReceivedDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = "completed",
            Items = [new GrnItem { Id = 704, PoItemId = 703, ReceivedQty = 100, UnitCost = 10m, TaxableAmount = 1000m }]
        });
        context.StockTransfers.Add(new StockTransfer
        {
            Id = Transfer,
            TransferNo = "TRF-CORR-601",
            TransferType = StockTransfer.TransferTypeDirect,
            FromLocationId = Warehouse,
            FromLocationType = "warehouse",
            ToLocationId = Outlet,
            ToLocationType = "outlet",
            TransferDate = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            Status = StockTransfer.StatusReceived,
            CreatedBy = Actor,
            Items = [new StockTransferItem { VariantId = Variant, Quantity = 20, RequestedQuantity = 20, TransferQuantity = 20, AcceptedQuantity = 20 }]
        });
        await context.SaveChangesAsync();

        context.StockLedgers.AddRange(
            Row(Variant, Warehouse, "warehouse", StockLedgerTransactionType.Grn, StockLedgerReferenceType.Grn, Grn, 100, 0, 100, 0m, false, new DateTime(2026, 3, 1), 1));
        await context.SaveChangesAsync();
        context.StockLedgers.Add(
            Row(Variant, Warehouse, "warehouse", StockLedgerTransactionType.TransferOut, StockLedgerReferenceType.StockTransfer, Transfer, 0, 20, 80, 0m, true, new DateTime(2026, 3, 2), 2));
        await context.SaveChangesAsync();
        context.StockLedgers.Add(
            Row(Variant, Outlet, "outlet", StockLedgerTransactionType.TransferIn, StockLedgerReferenceType.StockTransfer, Transfer, 20, 0, 20, 0m, true, new DateTime(2026, 3, 2), 1));
        await context.SaveChangesAsync();
        context.StockLedgers.Add(
            Row(Variant, Outlet, "outlet", StockLedgerTransactionType.Sale, StockLedgerReferenceType.Sale, 1, 0, 5, 15, 10m, true, new DateTime(2026, 3, 3), 2));

        context.ChartAccounts.AddRange(
            new ChartAccount { Id = InventoryAccount, BusinessId = Business, AccountCode = "1100", AccountName = "Inventory", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true },
            new ChartAccount { Id = 901, BusinessId = Business, AccountCode = "2000", AccountName = "Payables", AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit, IsActive = true },
            new ChartAccount { Id = 902, BusinessId = Business, AccountCode = "5000", AccountName = "COGS", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true });
        context.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = Business,
            InventoryAccountId = InventoryAccount,
            SupplierPayableAccountId = 901,
            CogsAccountId = 902,
            JournalNumberPrefix = "JR",
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear
        });
        context.FinancialYears.Add(new FinancialYear { Id = 910, BusinessId = Business, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true });
        context.AccountingPeriods.Add(new AccountingPeriod { Id = 911, FinancialYearId = 910, PeriodNumber = 3, Name = "Mar", StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2026, 3, 31), Status = AccountingPeriodStatuses.Open });
        context.JournalEntries.AddRange(
            Journal("JR-1", "PurchaseReceiptPosting", "Grn", Grn.ToString(), new DateTime(2026, 3, 1), InventoryAccount, 901, 1000m),
            Journal("JR-2", "SalePosting", "Sale", "1", new DateTime(2026, 3, 3), 902, InventoryAccount, 50m));
        await context.SaveChangesAsync();
    }

    private static JournalEntry Journal(
        string number,
        string type,
        string referenceType,
        string referenceId,
        DateTime date,
        long debitAccount,
        long creditAccount,
        decimal amount) => new()
    {
        BusinessId = Business,
        JournalNumber = number,
        JournalType = type,
        TransactionDate = date,
        PostingDate = date,
        BusinessDate = date,
        FinancialYearId = 910,
        AccountingPeriodId = 911,
        ReferenceType = referenceType,
        ReferenceId = referenceId,
        Status = JournalEntryStatuses.Posted,
        IsSystemGenerated = true,
        Lines =
        [
            new JournalEntryLine { AccountId = debitAccount, DebitAmount = amount },
            new JournalEntryLine { AccountId = creditAccount, CreditAmount = amount }
        ]
    };
}
