using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;
using RetailPOS.Infrastructure.StockTransfers;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

/// <summary>
/// Cost through the transfer lifecycle on real PostgreSQL: the server takes it from the source pool, value is conserved
/// across dispatch, receipt and return, unknown cost stays unknown, and no journal is created.
/// </summary>
public sealed class StockCostPostingPostgresTests
{
    private const long BusinessId = 1;
    private const long OtherBusinessId = 2;
    private const long ActorId = 101;
    private const long VariantId = 301;
    private const long ForeignVariantId = 302;
    private const long Warehouse = 21;
    private const long Outlet = 11;
    private const long TransferId = 601;

    [Fact]
    public async Task Dispatch_UsesSourceWeightedAverage_NotTheClientValue_AndReceiptConservesValue() =>
        await RunAsync(async database =>
        {
            await SeedStockAsync(
                database,
                20,
                StockCostTestData.Receipt(VariantId, Warehouse, "warehouse", 10, 5m, sequence: 1, referenceId: 900),
                StockCostTestData.Receipt(VariantId, Warehouse, "warehouse", 10, 7m, sequence: 2, referenceId: 901));
            await SeedTransferAsync(database, quantity: 5, clientUnitCost: 999m);

            await DispatchAsync(database);

            var dispatched = await TransferRowAsync(database, Warehouse, "warehouse");
            Assert.True(dispatched.IsCostValuationReliable);
            Assert.Equal(6m, dispatched.UnitCost);
            Assert.Equal(30m, dispatched.MovementValue);
            Assert.Equal(6m, await ItemCostAsync(database));

            await ReceiveAsync(database, TransferId, null);

            var received = await TransferRowAsync(database, Outlet, "outlet");
            Assert.True(received.IsCostValuationReliable);
            Assert.Equal(6m, received.UnitCost);
            Assert.Equal(30m, received.MovementValue);

            var source = await PositionAsync(database, Warehouse, "warehouse", 15);
            var destination = await PositionAsync(database, Outlet, "outlet", 5);
            Assert.Equal((StockCostKind.Valued, 90m), (source.Kind, source.Value));
            Assert.Equal((StockCostKind.Valued, 30m), (destination.Kind, destination.Value));
            await AssertNoJournalsAsync(database);
        });

    [Fact]
    public async Task PartialReceiptAndReturn_AccountForEveryPaisa() =>
        await RunAsync(async database =>
        {
            var receipt = StockCostTestData.Receipt(VariantId, Warehouse, "warehouse", 3, 3.3333m);
            receipt.MovementValue = 10m;
            await SeedStockAsync(database, 3, receipt);
            await SeedTransferAsync(database, quantity: 3);

            await DispatchAsync(database);
            Assert.Equal(10m, (await TransferRowAsync(database, Warehouse, "warehouse")).MovementValue);

            await ReceiveAsync(database, TransferId, [new StockTransferReceiveItemInput(VariantId, 1, 1, "Broken", 0, 1)]);
            Assert.Equal(3.33m, (await TransferRowAsync(database, Outlet, "outlet")).MovementValue);

            long returnId;
            await using (var context = CreateContext(database))
            {
                returnId = await context.StockTransfers
                    .Where(transfer => transfer.OriginalTransferId == TransferId)
                    .Select(transfer => transfer.Id)
                    .SingleAsync();
            }

            await ReceiveAsync(database, returnId, null);

            await using var verify = CreateContext(database);
            var returned = await verify.StockLedgers.SingleAsync(row =>
                row.ReferenceId == returnId && row.TransactionType == StockLedgerTransactionType.ReturnTransferIn);
            Assert.True(returned.IsCostValuationReliable);
            Assert.Equal(3.34m, returned.MovementValue);

            // 3.33 accepted + 3.34 returned + 3.33 damaged = the 10.00 that left the source.
            var source = await PositionAsync(database, Warehouse, "warehouse", 1);
            var destination = await PositionAsync(database, Outlet, "outlet", 1);
            Assert.Equal(3.34m, source.Value);
            Assert.Equal(3.33m, destination.Value);
            await AssertNoJournalsAsync(database);
        });

    [Fact]
    public async Task SourceWithoutLedgerHistory_MovesUnvalued_AndClientValueIsIgnored() =>
        await RunAsync(async database =>
        {
            await SeedStockAsync(database, 10);
            await SeedTransferAsync(database, quantity: 4, clientUnitCost: 999m);

            await DispatchAsync(database);
            await ReceiveAsync(database, TransferId, null);

            await AssertUnvaluedAsync(database);
            Assert.Equal(StockCostKind.Unvalued, (await PositionAsync(database, Outlet, "outlet", 4)).Kind);
        });

    [Fact]
    public async Task SourceHoldingAnUnprovenZero_MovesUnvalued() =>
        await RunAsync(async database =>
        {
            // Old data: a row claims reliability at zero, and no document says the stock was free.
            await SeedStockAsync(database, 10, StockCostTestData.Receipt(VariantId, Warehouse, "warehouse", 10, 0m));
            await SeedTransferAsync(database, quantity: 4);

            await DispatchAsync(database);
            await ReceiveAsync(database, TransferId, null);

            await AssertUnvaluedAsync(database);
        });

    [Fact]
    public async Task SourceThatHadAnUnreliableReceipt_MovesUnvalued() =>
        await RunAsync(async database =>
        {
            await SeedStockAsync(
                database,
                20,
                StockCostTestData.Receipt(VariantId, Warehouse, "warehouse", 10, 0m, sequence: 1, referenceId: 900, reliable: false),
                StockCostTestData.Receipt(VariantId, Warehouse, "warehouse", 10, 7m, sequence: 2, referenceId: 901));
            await SeedTransferAsync(database, quantity: 4);

            await DispatchAsync(database);
            await ReceiveAsync(database, TransferId, null);

            await AssertUnvaluedAsync(database);
        });

    [Fact]
    public async Task DocumentedFreeStock_MovesAtAVerifiedZero() =>
        await RunAsync(async database =>
        {
            await SeedStockAsync(database, 10, StockCostTestData.Receipt(VariantId, Warehouse, "warehouse", 10, 0m, referenceId: 900));
            await SeedFreeReceiptDocumentsAsync(database, grnId: 900);
            await SeedTransferAsync(database, quantity: 4);

            await DispatchAsync(database);
            await ReceiveAsync(database, TransferId, null);

            var dispatched = await TransferRowAsync(database, Warehouse, "warehouse");
            var received = await TransferRowAsync(database, Outlet, "outlet");
            Assert.True(dispatched.IsCostValuationReliable);
            Assert.True(received.IsCostValuationReliable);
            Assert.Equal(0m, dispatched.MovementValue);
            Assert.Equal(0m, received.MovementValue);
        });

    [Fact]
    public async Task TransferAlreadyInTransitAtZero_IsReceivedUnvalued_UnlessItsOriginIsProvenFree() =>
        await RunAsync(async database =>
        {
            // Dispatched before this rule existed: the row says reliable at zero, and the source held priced stock.
            await SeedStockAsync(database, 6, StockCostTestData.Receipt(VariantId, Warehouse, "warehouse", 10, 7m));
            await SeedTransferAsync(database, quantity: 4, status: StockTransfer.StatusInTransit);
            await using (var seed = CreateContext(database))
            {
                seed.StockLedgers.Add(new StockLedger
                {
                    VariantId = VariantId,
                    LocationId = Warehouse,
                    LocationType = "warehouse",
                    TransactionType = StockLedgerTransactionType.TransferOut,
                    QtyOut = 4,
                    BalanceAfter = 6,
                    UnitCost = 0m,
                    MovementValue = 0m,
                    IsCostValuationReliable = true,
                    BusinessDate = new DateTime(2026, 1, 2),
                    MovementSequence = 2,
                    ReferenceType = StockLedgerReferenceType.StockTransfer,
                    ReferenceId = TransferId,
                    CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
                });
                await seed.SaveChangesAsync();
            }

            await ReceiveAsync(database, TransferId, null);

            Assert.False((await TransferRowAsync(database, Outlet, "outlet")).IsCostValuationReliable);
        });

    [Fact]
    public async Task AnotherBusinessesCostHistory_IsNeverUsed() =>
        await RunAsync(async database =>
        {
            await using (var seed = CreateContext(database))
            {
                seed.Inventories.Add(new InventoryEntity
                {
                    VariantId = ForeignVariantId,
                    LocationId = Warehouse,
                    LocationType = "warehouse",
                    Quantity = 10
                });
                seed.StockLedgers.Add(StockCostTestData.Receipt(ForeignVariantId, Warehouse, "warehouse", 10, 9m));
                await seed.SaveChangesAsync();
            }

            await SeedTransferAsync(database, quantity: 4, variantId: ForeignVariantId);

            await DispatchAsync(database);

            await using var verify = CreateContext(database);
            var row = await verify.StockLedgers.SingleAsync(candidate =>
                candidate.ReferenceId == TransferId && candidate.VariantId == ForeignVariantId);
            Assert.False(row.IsCostValuationReliable);
            Assert.Equal(0m, row.UnitCost);
        });

    private static async Task AssertUnvaluedAsync(PostgresTestDatabase database)
    {
        var dispatched = await TransferRowAsync(database, Warehouse, "warehouse");
        var received = await TransferRowAsync(database, Outlet, "outlet");
        Assert.False(dispatched.IsCostValuationReliable);
        Assert.False(received.IsCostValuationReliable);
        Assert.Equal(0m, dispatched.MovementValue);
        Assert.Equal(0m, received.MovementValue);
        Assert.Equal(0m, await ItemCostAsync(database));
    }

    private static async Task AssertNoJournalsAsync(PostgresTestDatabase database)
    {
        await using var context = CreateContext(database);
        Assert.Empty(await context.JournalEntries.ToListAsync());
    }

    private static async Task<decimal> ItemCostAsync(PostgresTestDatabase database)
    {
        await using var context = CreateContext(database);
        return await context.StockTransferItems
            .Where(item => item.TransferId == TransferId)
            .Select(item => item.UnitCost)
            .SingleAsync();
    }

    private static async Task<StockLedger> TransferRowAsync(
        PostgresTestDatabase database,
        long locationId,
        string locationType)
    {
        await using var context = CreateContext(database);
        return await context.StockLedgers.AsNoTracking().SingleAsync(row =>
            row.ReferenceType == StockLedgerReferenceType.StockTransfer
            && row.ReferenceId == TransferId
            && row.LocationId == locationId
            && row.LocationType == locationType
            && (row.QtyIn > 0 || row.QtyOut > 0));
    }

    private static async Task<StockCostPosition> PositionAsync(
        PostgresTestDatabase database,
        long locationId,
        string locationType,
        int onHand)
    {
        await using var context = CreateContext(database);
        var rows = await context.StockLedgers.AsNoTracking()
            .Where(row => row.VariantId == VariantId)
            .OrderBy(row => row.BusinessDate)
            .ThenBy(row => row.MovementSequence)
            .ThenBy(row => row.Id)
            .ToListAsync();
        var ledger = new StockCostLedger();
        foreach (var row in rows)
        {
            ledger.Apply(new StockCostMovement(
                row.VariantId,
                row.LocationId,
                row.LocationType,
                row.ReferenceType,
                row.ReferenceId,
                row.QtyIn,
                row.QtyOut,
                row.UnitCost,
                row.MovementValue,
                row.IsCostValuationReliable));
        }

        return ledger.Classify(VariantId, new StockCostLocation(locationId, locationType), onHand, StockCostEvidence.None);
    }

    private static async Task DispatchAsync(PostgresTestDatabase database)
    {
        await using var context = CreateContext(database);
        await new StockTransferDispatchStore(
                context,
                new StockLedgerWriter(context, NullLogger<StockLedgerWriter>.Instance),
                NullLogger<StockTransferDispatchStore>.Instance)
            .SubmitAndDispatchAsync(TransferId, BusinessId, ActorId, autoApprove: true, CancellationToken.None);
    }

    private static async Task ReceiveAsync(
        PostgresTestDatabase database,
        long transferId,
        IReadOnlyList<StockTransferReceiveItemInput>? lines)
    {
        await using var context = CreateContext(database);
        await new StockTransferReceiveStore(
                context,
                new StockLedgerWriter(context, NullLogger<StockLedgerWriter>.Instance),
                Mock.Of<IStockTransferReceiptAuditWriter>(),
                NullLogger<StockTransferReceiveStore>.Instance)
            .ReceiveAsync(
                transferId,
                BusinessId,
                lines is null ? null : new StockTransferReceiveInput(lines, null),
                ActorId,
                CancellationToken.None);
    }

    private static async Task SeedStockAsync(PostgresTestDatabase database, int onHand, params StockLedger[] history)
    {
        await using var context = CreateContext(database);
        context.Inventories.Add(new InventoryEntity
        {
            VariantId = VariantId,
            LocationId = Warehouse,
            LocationType = "warehouse",
            Quantity = onHand
        });
        context.StockLedgers.AddRange(history);
        await context.SaveChangesAsync();
    }

    private static async Task SeedTransferAsync(
        PostgresTestDatabase database,
        int quantity,
        decimal clientUnitCost = 0m,
        string status = StockTransfer.StatusDraft,
        long variantId = VariantId)
    {
        await using var context = CreateContext(database);
        context.StockTransfers.Add(new StockTransfer
        {
            Id = TransferId,
            TransferNo = "TRF-COST-601",
            TransferType = StockTransfer.TransferTypeDirect,
            FromLocationId = Warehouse,
            FromLocationType = "warehouse",
            ToLocationId = Outlet,
            ToLocationType = "outlet",
            TransferDate = DateTime.UtcNow,
            Status = status,
            CreatedBy = ActorId,
            Items =
            [
                new StockTransferItem
                {
                    VariantId = variantId,
                    Quantity = quantity,
                    RequestedQuantity = quantity,
                    TransferQuantity = quantity,
                    UnitCost = clientUnitCost
                }
            ]
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedFreeReceiptDocumentsAsync(PostgresTestDatabase database, long grnId)
    {
        await using var context = CreateContext(database);
        context.Suppliers.Add(new Supplier { Id = 701, BusinessId = BusinessId, Name = "Supplier" });
        context.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 702,
            PoNumber = "PO-FREE-1",
            SupplierId = 701,
            WarehouseId = Warehouse,
            OrderDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = "completed",
            Items = [new PurchaseOrderItem { Id = 703, VariantId = VariantId, Quantity = 10, UnitPrice = 0m }]
        });
        context.Grns.Add(new Grn
        {
            Id = grnId,
            PoId = 702,
            ReceivedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = "completed",
            Items = [new GrnItem { Id = 704, PoItemId = 703, ReceivedQty = 10, UnitCost = 0m }]
        });
        await context.SaveChangesAsync();
    }

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

    private static async Task SeedAsync(RetailPOSDbContext context)
    {
        context.Businesses.AddRange(
            new Business { Id = BusinessId, Name = "Cost business", IsActive = true },
            new Business { Id = OtherBusinessId, Name = "Other business", IsActive = true });
        context.BusinessPosSettings.Add(new BusinessPosSettings
        {
            BusinessId = BusinessId,
            DefaultTimeZoneId = "Asia/Dhaka"
        });
        context.Users.Add(new User
        {
            Id = ActorId,
            BusinessId = BusinessId,
            Name = "Transfer user",
            Email = "cost@example.test",
            PasswordHash = "test-only",
            IsActive = true
        });
        context.Categories.AddRange(
            new Category { Id = 201, BusinessId = BusinessId, Name = "Cost" },
            new Category { Id = 202, BusinessId = OtherBusinessId, Name = "Other" });
        context.Outlets.Add(new Outlet { Id = Outlet, BusinessId = BusinessId, Name = "Destination", Address = "Test" });
        context.Warehouses.Add(new Warehouse { Id = Warehouse, BusinessId = BusinessId, Name = "Source", Address = "Test" });
        context.Products.AddRange(
            new Product { Id = 300, BusinessId = BusinessId, Name = "Cost product", ProductCode = "COST-1", CategoryId = 201, CostPrice = 4000m, BasePrice = 5000m, Status = ProductStatus.Active },
            new Product { Id = 310, BusinessId = OtherBusinessId, Name = "Other product", ProductCode = "COST-2", CategoryId = 202, Status = ProductStatus.Active });
        context.ProductVariants.AddRange(
            new ProductVariant { Id = VariantId, BusinessId = BusinessId, ProductId = 300, Name = "A", Sku = "COST-A", Attributes = "{}" },
            new ProductVariant { Id = ForeignVariantId, BusinessId = OtherBusinessId, ProductId = 310, Name = "B", Sku = "COST-B", Attributes = "{}" });
        await context.SaveChangesAsync();
    }
}
