using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;
using RetailPOS.Infrastructure.StockTransfers;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

/// <summary>
/// Rejected and short-received transfers on real PostgreSQL: full reconciliation, return transit, source stock
/// restored only on physical receipt, audit in the same transaction, and no duplicate movement.
/// </summary>
public sealed class StockTransferReconciliationPostgresTests
{
    private const long BusinessId = 1;
    private const long ActorId = 101;
    private const long VariantId = 301;
    private const long OtherVariantId = 302;
    private const long Warehouse = 21;
    private const long Outlet = 11;
    private const long TransferId = 601;
    private const decimal SourceStock = 20;
    private const decimal DestinationStock = 5;
    private const int Dispatched = 10;

    [Fact]
    public async Task PartialReceipt_MovesOnlyAcceptedUnits_AndReturnRestoresSourceOnlyWhenReceived() =>
        await RunAsync(async database =>
        {
            await using (var seed = CreateContext(database))
            {
                seed.StockLedgers.Add(StockCostTestData.Receipt(VariantId, Warehouse, "warehouse", SourceStock, 7.5m));
                await seed.SaveChangesAsync();
            }

            await DispatchAsync(database);
            Assert.Equal((SourceStock - Dispatched, DestinationStock), await StockAsync(database));
            var inTransit = await ReadAsync(database, TransferId);
            Assert.Equal(Dispatched, inTransit.Items.Single().InTransitQuantity);
            Assert.Equal(0, inTransit.Items.Single().ReturnTransitQuantity);

            var audit = new Mock<IStockTransferReceiptAuditWriter>();
            await ReceiveAsync(database, TransferId, [Line(6, 2, 1, 1, "Carton crushed; one unit lost")], audit.Object);

            Assert.Equal((SourceStock - Dispatched, DestinationStock + 6), await StockAsync(database));
            var received = await ReadAsync(database, TransferId);
            Assert.Equal(StockTransfer.StatusPartiallyReceived, received.Status);
            var line = received.Items.Single();
            Assert.Equal((6, 2, 1, 1), (line.AcceptedQuantity, line.RejectedQuantity, line.MissingQuantity, line.DamagedQuantity));
            Assert.Equal(0, line.InTransitQuantity);
            Assert.Equal(2, line.ReturnTransitQuantity);
            Assert.NotNull(received.ReturnTransferId);
            audit.Verify(writer => writer.RecordAsync(
                It.Is<StockTransferReceiptAudit>(entry => entry.TransferId == TransferId
                    && entry.Lines.Single().MissingQuantity == 1
                    && entry.Lines.Single().DamagedQuantity == 1
                    && entry.Lines.Single().Remarks!.Contains("crushed")),
                It.IsAny<CancellationToken>()), Times.Once);

            var returnId = received.ReturnTransferId!.Value;
            var inReturn = await ReadAsync(database, returnId);
            Assert.Equal(StockTransfer.StatusInTransit, inReturn.Status);
            Assert.Equal(TransferId, inReturn.OriginalTransferId);
            Assert.Equal(2, inReturn.Items.Single().TransferQuantity);
            Assert.Equal(7.5m, inReturn.Items.Single().UnitCost);
            Assert.Equal(2, inReturn.Items.Single().ReturnTransitQuantity);
            Assert.Equal(0, inReturn.Items.Single().InTransitQuantity);

            var atOutlet = await LedgerAsync(database, Outlet, "outlet");
            Assert.Equal(
                new[]
                {
                    (StockLedgerTransactionType.TransferIn, (decimal)6, (decimal)0, (decimal)(DestinationStock + 6)),
                    (StockLedgerTransactionType.TransferRejection, (decimal)0, (decimal)0, (decimal)(DestinationStock + 6)),
                    (StockLedgerTransactionType.TransferDiscrepancy, (decimal)0, (decimal)0, (decimal)(DestinationStock + 6))
                },
                atOutlet.Select(row => (row.TransactionType, row.QtyIn, row.QtyOut, row.BalanceAfter)));
            Assert.Equal(new long[] { 1, 2, 3 }, atOutlet.Select(row => row.MovementSequence));

            await ReceiveAsync(database, returnId, [Line(1, 0, 1, 0, "One unit not on the truck")]);

            Assert.Equal((SourceStock - Dispatched + 1, DestinationStock + 6), await StockAsync(database));
            var returned = await ReadAsync(database, TransferId);
            Assert.Equal(0, returned.Items.Single().ReturnTransitQuantity);
            var atWarehouse = await LedgerAsync(database, Warehouse, "warehouse");
            Assert.Equal(
                new[]
                {
                    (StockLedgerTransactionType.Grn, (decimal)SourceStock, (decimal)0, (decimal)(SourceStock)),
                    (StockLedgerTransactionType.TransferOut, (decimal)0, (decimal)Dispatched, (decimal)(SourceStock - Dispatched)),
                    (StockLedgerTransactionType.ReturnTransferIn, (decimal)1, (decimal)0, (decimal)(SourceStock - Dispatched + 1)),
                    (StockLedgerTransactionType.TransferDiscrepancy, (decimal)0, (decimal)0, (decimal)(SourceStock - Dispatched + 1))
                },
                atWarehouse.Select(row => (row.TransactionType, row.QtyIn, row.QtyOut, row.BalanceAfter)));
            Assert.Equal(75m, atWarehouse[1].MovementValue);
            Assert.Equal(7.5m, atWarehouse[2].UnitCost);
            Assert.Equal(7.5m, atWarehouse[2].MovementValue);

            // The system return was never dispatched, so the destination lost nothing to it.
            Assert.DoesNotContain(
                await LedgerAsync(database, Outlet, "outlet"),
                row => row.TransactionType == StockLedgerTransactionType.ReturnTransferOut);
        });

    [Fact]
    public async Task FullRejection_LeavesDestinationUnchanged_AndRestoresSourceOnReceipt() =>
        await RunAsync(async database =>
        {
            await DispatchAsync(database);
            var audit = new Mock<IStockTransferReceiptAuditWriter>();
            await using (var context = CreateContext(database))
            {
                await CreateRejectionStore(context, audit.Object)
                    .RejectAsync(TransferId, BusinessId, "Wrong goods", ActorId, CancellationToken.None);
            }

            Assert.Equal((SourceStock - Dispatched, DestinationStock), await StockAsync(database));
            audit.Verify(writer => writer.RecordAsync(
                It.Is<StockTransferReceiptAudit>(entry => entry.Reason == "Wrong goods"),
                It.IsAny<CancellationToken>()), Times.Once);
            var rejected = await ReadAsync(database, TransferId);
            Assert.Equal(StockTransfer.StatusRejected, rejected.Status);
            Assert.Equal(Dispatched, rejected.Items.Single().ReturnTransitQuantity);

            await ReceiveAsync(database, rejected.ReturnTransferId!.Value, null);

            Assert.Equal((SourceStock, DestinationStock), await StockAsync(database));
            var completed = await ReadAsync(database, rejected.ReturnTransferId!.Value);
            Assert.Equal(StockTransfer.StatusReceived, completed.Status);
        });

    [Theory]
    [InlineData(8, 1, 0, 0, "short by one")]
    [InlineData(10, 1, 0, 0, "over by one")]
    [InlineData(9, 0, 1, 0, null)]
    [InlineData(9, 0, 0, 1, "  ")]
    [InlineData(-1, 11, 0, 0, null)]
    public async Task IncompleteReconciliation_ChangesNothing(
        int accepted,
        int rejected,
        int missing,
        int damaged,
        string? remarks) =>
        await RunAsync(async database =>
        {
            await DispatchAsync(database);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReceiveAsync(database, TransferId, [Line(accepted, rejected, missing, damaged, remarks)]));
            await AssertStillInTransitAsync(database);
        });

    [Fact]
    public async Task EveryLineMustBeAccountedFor_ExactlyOnce() =>
        await RunAsync(async database =>
        {
            await DispatchAsync(database);
            await using (var seed = CreateContext(database))
            {
                seed.StockTransferItems.Add(new StockTransferItem
                {
                    TransferId = TransferId,
                    VariantId = OtherVariantId,
                    Quantity = 1,
                    RequestedQuantity = 1,
                    TransferQuantity = 1
                });
                await seed.SaveChangesAsync();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReceiveAsync(database, TransferId, [Line(Dispatched, 0, 0, 0, null)]));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReceiveAsync(database, TransferId, [Line(Dispatched, 0, 0, 0, null), Line(Dispatched, 0, 0, 0, null)]));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReceiveAsync(
                    database,
                    TransferId,
                    [
                        Line(Dispatched, 0, 0, 0, null),
                        new StockTransferReceiveItemInput(OtherVariantId, 1, 0, null),
                        new StockTransferReceiveItemInput(999, 1, 0, null)
                    ]));
            await AssertStillInTransitAsync(database);
        });

    [Fact]
    public async Task SystemReturn_CannotBeRejectedAgain_OrCancelled() =>
        await RunAsync(async database =>
        {
            await DispatchAsync(database);
            await ReceiveAsync(database, TransferId, [Line(8, 2, 0, 0, null)]);
            var returnId = (await ReadAsync(database, TransferId)).ReturnTransferId!.Value;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReceiveAsync(database, returnId, [Line(1, 1, 0, 0, null)]));
            await using (var context = CreateContext(database))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    CreateRejectionStore(context, Mock.Of<IStockTransferReceiptAuditWriter>())
                        .RejectAsync(returnId, BusinessId, "No", ActorId, CancellationToken.None));
            }

            await using (var context = CreateContext(database))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new StockTransferCancellationStore(context, NullLogger<StockTransferCancellationStore>.Instance)
                        .CancelAsync(returnId, BusinessId, "No", ActorId, CancellationToken.None));
            }

            Assert.Equal(StockTransfer.StatusInTransit, (await ReadAsync(database, returnId)).Status);
            Assert.Equal((SourceStock - Dispatched, DestinationStock + 8), await StockAsync(database));
        });

    [Fact]
    public async Task ConcurrentReceiptsOfTheReturn_RestoreSourceOnce() =>
        await RunAsync(async database =>
        {
            await DispatchAsync(database);
            await ReceiveAsync(database, TransferId, [Line(8, 2, 0, 0, null)]);
            var returnId = (await ReadAsync(database, TransferId)).ReturnTransferId!.Value;

            var outcomes = await Task.WhenAll(
                Task.Run(() => AttemptReceiveAsync(database, returnId)),
                Task.Run(() => AttemptReceiveAsync(database, returnId)));

            Assert.Single(outcomes, outcome => outcome is null);
            Assert.Single(outcomes, outcome => outcome is InvalidOperationException);
            Assert.Equal((SourceStock - Dispatched + 2, DestinationStock + 8), await StockAsync(database));
            Assert.Single(
                await LedgerAsync(database, Warehouse, "warehouse"),
                row => row.TransactionType == StockLedgerTransactionType.ReturnTransferIn);
        });

    [Fact]
    public async Task AuditFailure_RollsBackTheWholeReceipt() =>
        await RunAsync(async database =>
        {
            await DispatchAsync(database);
            var audit = new Mock<IStockTransferReceiptAuditWriter>();
            audit.Setup(writer => writer.RecordAsync(
                    It.IsAny<StockTransferReceiptAudit>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DataMisalignedException("audit unavailable"));

            await Assert.ThrowsAsync<DataMisalignedException>(() =>
                ReceiveAsync(database, TransferId, [Line(9, 0, 1, 0, "Lost in transit")], audit.Object));

            await AssertStillInTransitAsync(database);
        });

    [Fact]
    public async Task SecondSystemReturnForATransfer_IsRefusedByTheDatabase() =>
        await RunAsync(async database =>
        {
            await DispatchAsync(database);
            await ReceiveAsync(database, TransferId, [Line(8, 2, 0, 0, null)]);

            await using var context = CreateContext(database);
            context.StockTransfers.Add(new StockTransfer
            {
                TransferNo = "TRF-TEST-DUPLICATE",
                TransferType = StockTransfer.TransferTypeReturn,
                OriginalTransferId = TransferId,
                FromLocationId = Outlet,
                FromLocationType = "outlet",
                ToLocationId = Warehouse,
                ToLocationType = "warehouse",
                TransferDate = DateTime.UtcNow,
                Status = StockTransfer.StatusInTransit
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        });

    [Fact]
    public async Task EvidenceOnlyMovement_ConflictsWithAConcurrentStockChange() =>
        await RunAsync(async database =>
        {
            await DispatchAsync(database);
            await using var context = CreateContext(database);
            var store = new StockTransferReceiveStore(
                context,
                new InterleavingLedgerWriter(new StockLedgerWriter(context, NullLogger<StockLedgerWriter>.Instance), () =>
                {
                    // A sale commits at this outlet after the receipt read the row but before it saves.
                    using var other = CreateContext(database);
                    var row = other.Inventories.Single(item => item.VariantId == VariantId && item.LocationId == Outlet);
                    row.Quantity -= 1;
                    other.SaveChanges();
                }),
                Mock.Of<IStockTransferReceiptAuditWriter>(),
                NullLogger<StockTransferReceiveStore>.Instance);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.ReceiveAsync(
                TransferId,
                BusinessId,
                new StockTransferReceiveInput([Line(0, 10, 0, 0, null)], null),
                ActorId,
                CancellationToken.None));

            Assert.Equal(StockTransfer.StatusInTransit, (await ReadAsync(database, TransferId)).Status);
            Assert.Empty(await LedgerAsync(database, Outlet, "outlet"));
        });

    [Fact]
    public async Task Migration_RollsBackAndForward_AndExistingTransfersReadAsUndisturbed() =>
        await RunAsync(async database =>
        {
            await using (var context = CreateContext(database))
            {
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync("20261105204249_ExpenseEntryEnhancements");
                await migrator.MigrateAsync();
            }

            var transfer = await ReadAsync(database, TransferId);
            Assert.Equal(StockTransfer.StatusDraft, transfer.Status);
            Assert.Null(transfer.OriginalTransferId);
            Assert.Null(transfer.ReturnTransferId);
            var line = transfer.Items.Single();
            Assert.Equal((0, 0), (line.MissingQuantity, line.DamagedQuantity));
            Assert.Equal(Dispatched, line.TransferQuantity);
        });

    [Fact]
    public async Task LedgerWriter_DoesNotLoadTheTable_AndDatesMovementsInTheCompanyZone() =>
        await RunAsync(async database =>
        {
            await using (var seed = CreateContext(database))
            {
                seed.StockLedgers.AddRange(Enumerable.Range(1, 50).Select(index => new StockLedger
                {
                    VariantId = OtherVariantId,
                    LocationId = Outlet,
                    LocationType = "outlet",
                    TransactionType = StockLedgerTransactionType.Grn,
                    MovementSequence = index,
                    ReferenceType = StockLedgerReferenceType.Grn,
                    ReferenceId = index,
                    BusinessDate = DateTime.UtcNow.Date,
                    CreatedAt = DateTime.UtcNow
                }));
                await seed.SaveChangesAsync();
            }

            await using var context = CreateContext(database);
            var writer = new StockLedgerWriter(context, NullLogger<StockLedgerWriter>.Instance);
            void Write(long variantId, long locationId, string locationType, DateTime createdAt, DateTime? businessDate = null) =>
                writer.WriteEntry(
                    variantId,
                    locationId,
                    locationType,
                    StockLedgerTransactionType.Sale,
                    0,
                    1,
                    0,
                    StockLedgerReferenceType.Sale,
                    1,
                    createdAt: createdAt,
                    businessDate: businessDate);

            // 19:27Z on 5 November is 01:27 on 6 November in Dhaka.
            var afterLocalMidnight = new DateTime(2026, 11, 5, 19, 27, 0, DateTimeKind.Utc);
            Write(OtherVariantId, Outlet, "outlet", afterLocalMidnight);
            Write(OtherVariantId, Outlet, "outlet", afterLocalMidnight);
            Write(VariantId, Warehouse, "warehouse", afterLocalMidnight);
            Write(VariantId, Outlet, "outlet", afterLocalMidnight, new DateTime(2026, 1, 2));

            var written = context.StockLedgers.Local.ToArray();
            Assert.Equal(4, written.Length);
            Assert.Equal(new long[] { 51, 52, 1, 1 }, written.Select(row => row.MovementSequence));
            Assert.Equal(new DateTime(2026, 11, 6), written[0].BusinessDate);
            Assert.Equal(new DateTime(2026, 11, 6), written[2].BusinessDate);
            Assert.Equal(new DateTime(2026, 1, 2), written[3].BusinessDate);
        });

    private sealed class InterleavingLedgerWriter(IStockLedgerWriter inner, Action beforeWrite) : IStockLedgerWriter
    {
        public void WriteEntry(
            long variantId, long locationId, string locationType, string transactionType, decimal qtyIn, decimal qtyOut,
            decimal balanceAfter, string referenceType, long referenceId, string? remarks = null, long? createdBy = null,
            DateTime? createdAt = null, decimal? unitCost = null, decimal? movementValue = null,
            DateTime? businessDate = null, long? movementSequence = null, long? reversalOfMovementId = null)
        {
            beforeWrite();
            inner.WriteEntry(variantId, locationId, locationType, transactionType, qtyIn, qtyOut, balanceAfter,
                referenceType, referenceId, remarks, createdBy, createdAt, unitCost, movementValue, businessDate,
                movementSequence, reversalOfMovementId);
        }

        public void WriteEntryWithValuation(
            long variantId, long locationId, string locationType, string transactionType, decimal qtyIn, decimal qtyOut,
            decimal balanceAfter, string referenceType, long referenceId, string? remarks = null, long? createdBy = null,
            DateTime? createdAt = null, decimal? unitCost = null, decimal? movementValue = null,
            DateTime? businessDate = null, long? movementSequence = null, long? reversalOfMovementId = null)
        {
            beforeWrite();
            inner.WriteEntryWithValuation(variantId, locationId, locationType, transactionType, qtyIn, qtyOut,
                balanceAfter, referenceType, referenceId, remarks, createdBy, createdAt, unitCost, movementValue,
                businessDate, movementSequence, reversalOfMovementId);
        }

        public void WriteVerifiedZeroCostEntry(
            long variantId, long locationId, string locationType, string transactionType, decimal qtyIn, decimal qtyOut,
            decimal balanceAfter, string referenceType, long referenceId, string? remarks = null, long? createdBy = null,
            DateTime? createdAt = null) =>
            inner.WriteVerifiedZeroCostEntry(variantId, locationId, locationType, transactionType, qtyIn, qtyOut,
                balanceAfter, referenceType, referenceId, remarks, createdBy, createdAt);
    }

    private static StockTransferReceiveItemInput Line(
        int accepted,
        int rejected,
        int missing,
        int damaged,
        string? remarks) =>
        new(VariantId, accepted, rejected, remarks, missing, damaged);

    private static async Task AssertStillInTransitAsync(PostgresTestDatabase database)
    {
        var transfer = await ReadAsync(database, TransferId);
        Assert.Equal(StockTransfer.StatusInTransit, transfer.Status);
        Assert.Null(transfer.ReturnTransferId);
        Assert.Equal((SourceStock - Dispatched, DestinationStock), await StockAsync(database));
        Assert.Empty(await LedgerAsync(database, Outlet, "outlet"));
    }

    private static async Task<Exception?> AttemptReceiveAsync(PostgresTestDatabase database, long transferId)
    {
        try
        {
            await ReceiveAsync(database, transferId, null);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
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
        IReadOnlyList<StockTransferReceiveItemInput>? lines,
        IStockTransferReceiptAuditWriter? audit = null)
    {
        await using var context = CreateContext(database);
        await new StockTransferReceiveStore(
                context,
                new StockLedgerWriter(context, NullLogger<StockLedgerWriter>.Instance),
                audit ?? Mock.Of<IStockTransferReceiptAuditWriter>(),
                NullLogger<StockTransferReceiveStore>.Instance)
            .ReceiveAsync(
                transferId,
                BusinessId,
                lines is null ? null : new StockTransferReceiveInput(lines, null),
                ActorId,
                CancellationToken.None);
    }

    private static StockTransferRejectionStore CreateRejectionStore(
        RetailPOSDbContext context,
        IStockTransferReceiptAuditWriter audit) =>
        new(
            context,
            new StockLedgerWriter(context, NullLogger<StockLedgerWriter>.Instance),
            audit,
            NullLogger<StockTransferRejectionStore>.Instance);

    private static async Task<StockTransferReadModel> ReadAsync(PostgresTestDatabase database, long transferId)
    {
        await using var context = CreateContext(database);
        return await new StockTransferReadStore(context).GetByIdAsync(transferId, BusinessId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Transfer {transferId} not found.");
    }

    private static async Task<(decimal Source, decimal Destination)> StockAsync(PostgresTestDatabase database)
    {
        await using var context = CreateContext(database);
        var rows = await context.Inventories.AsNoTracking()
            .Where(row => row.VariantId == VariantId)
            .ToListAsync();
        return (
            rows.Single(row => row.LocationId == Warehouse && row.LocationType == "warehouse").Quantity,
            rows.Single(row => row.LocationId == Outlet && row.LocationType == "outlet").Quantity);
    }

    private static async Task<StockLedger[]> LedgerAsync(
        PostgresTestDatabase database,
        long locationId,
        string locationType)
    {
        await using var context = CreateContext(database);
        return await context.StockLedgers.AsNoTracking()
            .Where(row => row.VariantId == VariantId
                && row.LocationId == locationId
                && row.LocationType == locationType)
            .OrderBy(row => row.MovementSequence)
            .ToArrayAsync();
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
        context.Businesses.Add(new Business { Id = BusinessId, Name = "Reconciliation business", IsActive = true });
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
            Email = "reconciliation@example.test",
            PasswordHash = "test-only",
            IsActive = true
        });
        context.Categories.Add(new Category { Id = 201, BusinessId = BusinessId, Name = "Reconciliation" });
        context.Outlets.Add(new Outlet { Id = Outlet, BusinessId = BusinessId, Name = "Destination", Address = "Test" });
        context.Warehouses.Add(new Warehouse { Id = Warehouse, BusinessId = BusinessId, Name = "Source", Address = "Test" });
        context.Products.Add(new Product
        {
            Id = 300,
            BusinessId = BusinessId,
            Name = "Reconciliation product",
            ProductCode = "REC-001",
            CategoryId = 201,
            Status = ProductStatus.Active
        });
        context.ProductVariants.AddRange(
            new ProductVariant { Id = VariantId, BusinessId = BusinessId, ProductId = 300, Name = "A", Sku = "REC-A", Attributes = "{}" },
            new ProductVariant { Id = OtherVariantId, BusinessId = BusinessId, ProductId = 300, Name = "B", Sku = "REC-B", Attributes = "{}" });
        context.Inventories.AddRange(
            new InventoryEntity { VariantId = VariantId, LocationId = Warehouse, LocationType = "warehouse", Quantity = SourceStock },
            new InventoryEntity { VariantId = VariantId, LocationId = Outlet, LocationType = "outlet", Quantity = DestinationStock });
        context.StockTransfers.Add(new StockTransfer
        {
            Id = TransferId,
            TransferNo = "TRF-TEST-601",
            TransferType = StockTransfer.TransferTypeDirect,
            FromLocationId = Warehouse,
            FromLocationType = "warehouse",
            ToLocationId = Outlet,
            ToLocationType = "outlet",
            TransferDate = DateTime.UtcNow,
            Status = StockTransfer.StatusDraft,
            CreatedBy = ActorId,
            Items =
            [
                new StockTransferItem
                {
                    VariantId = VariantId,
                    Quantity = Dispatched,
                    RequestedQuantity = Dispatched,
                    TransferQuantity = Dispatched,
                    UnitCost = 7.5m
                }
            ]
        });
        await context.SaveChangesAsync();
    }
}
