using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Commands;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;
using RetailPOS.Infrastructure.StockTransfers;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

public sealed class StockTransferSubmitAndDispatchPostgresConcurrencyTests
{
    static StockTransferSubmitAndDispatchPostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentSubmitAndDispatch_ProducesOneMovement_AndLedgerFailureRollsBack()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockTransferDispatch,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    await SeedAsync(seed);
                }

                using var barrier = new Barrier(2);
                await using var firstContext = CreateContext(database.ConnectionString);
                await using var secondContext = CreateContext(database.ConnectionString);
                var cacheInvalidator = CreateCacheInvalidator();
                var firstHandler = CreateHandler(
                    firstContext,
                    new DispatchBarrierStockLedgerWriter(
                        CreateStockLedgerWriter(firstContext),
                        barrier),
                    cacheInvalidator.Object);
                var secondHandler = CreateHandler(
                    secondContext,
                    new DispatchBarrierStockLedgerWriter(
                        CreateStockLedgerWriter(secondContext),
                        barrier),
                    cacheInvalidator.Object);

                var outcomes = await Task.WhenAll(
                    Task.Run(() => AttemptSubmitAndDispatchAsync(firstHandler, 601)),
                    Task.Run(() => AttemptSubmitAndDispatchAsync(secondHandler, 601)));

                Assert.Single(outcomes, outcome => outcome.Exception is null);
                Assert.Single(outcomes, outcome => outcome.Exception is DbUpdateException);

                await using (var verify = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    var transfer = await verify.StockTransfers.SingleAsync(row => row.Id == 601);
                    Assert.Equal(StockTransfer.StatusInTransit, transfer.Status);
                    Assert.Equal(101, transfer.SubmittedBy);
                    Assert.Equal(101, transfer.ApprovedBy);
                    Assert.Equal(101, transfer.DispatchedBy);
                    Assert.NotNull(transfer.SubmittedAt);
                    Assert.NotNull(transfer.ApprovedAt);
                    Assert.NotNull(transfer.DispatchedAt);
                    Assert.Equal(
                        8,
                        await verify.Inventories
                            .Where(row => row.VariantId == 301
                                && row.LocationId == 11
                                && row.LocationType == "outlet")
                            .Select(row => row.Quantity)
                            .SingleAsync());
                    var ledger = await verify.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                            && row.ReferenceId == 601
                            && row.TransactionType == StockLedgerTransactionType.TransferOut)
                        .ToArrayAsync();
                    Assert.Single(ledger);
                    Assert.Equal(2, ledger[0].QtyOut);
                    Assert.Equal(8, ledger[0].BalanceAfter);

                    await verify.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE stock_ledgers ADD CONSTRAINT ck_stock_transfer_dispatch_test_failure CHECK (qty_in < 0) NOT VALID");
                }

                await using (var replayContext = CreateContext(database.ConnectionString))
                {
                    var replayHandler = CreateHandler(
                        replayContext,
                        CreateStockLedgerWriter(replayContext),
                        cacheInvalidator.Object);
                    var replay = await SubmitAndDispatchAsync(replayHandler, 601);
                    Assert.Equal(StockTransfer.StatusInTransit, replay.Status);
                }

                await using (var rollbackContext = CreateContext(database.ConnectionString))
                {
                    var rollbackHandler = CreateHandler(
                        rollbackContext,
                        CreateStockLedgerWriter(rollbackContext),
                        cacheInvalidator.Object);
                    await Assert.ThrowsAsync<DbUpdateException>(() =>
                        SubmitAndDispatchAsync(rollbackHandler, 602));
                }

                await using (var final = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    var transfer = await final.StockTransfers.SingleAsync(row => row.Id == 602);
                    Assert.Equal(StockTransfer.StatusDraft, transfer.Status);
                    Assert.Null(transfer.SubmittedAt);
                    Assert.Null(transfer.ApprovedAt);
                    Assert.Null(transfer.DispatchedAt);
                    Assert.Equal(
                        8,
                        await final.Inventories
                            .Where(row => row.VariantId == 301
                                && row.LocationId == 11
                                && row.LocationType == "outlet")
                            .Select(row => row.Quantity)
                            .SingleAsync());
                    Assert.Empty(await final.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                            && row.ReferenceId == 602)
                        .ToArrayAsync());
                }

                cacheInvalidator.Verify(value => value.InvalidateAsync(
                    301,
                    11,
                    It.IsAny<CancellationToken>()), Times.Once);
            });
    }

    [Fact]
    public async Task ConcurrentStandaloneSend_ProducesOneMovement_AndLedgerFailureRollsBack()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockTransferDispatch,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    await SeedAsync(seed);
                    var timestamp = DateTime.UtcNow;
                    foreach (var transferId in new long[] { 601, 602 })
                    {
                        var transfer = await seed.StockTransfers.SingleAsync(row => row.Id == transferId);
                        transfer.Status = StockTransfer.StatusApproved;
                        transfer.ApprovedBy = 101;
                        transfer.ApprovedAt = timestamp;
                        transfer.UpdatedBy = 101;
                        transfer.UpdatedAt = timestamp;
                    }

                    await seed.SaveChangesAsync();
                }

                using var barrier = new Barrier(2);
                await using var firstContext = CreateContext(database.ConnectionString);
                await using var secondContext = CreateContext(database.ConnectionString);
                var cacheInvalidator = CreateCacheInvalidator();
                var outcomes = await Task.WhenAll(
                    Task.Run(() => AttemptSendAsync(
                        CreateSendHandler(
                            firstContext,
                            new DispatchBarrierStockLedgerWriter(
                                CreateStockLedgerWriter(firstContext),
                                barrier),
                            cacheInvalidator.Object),
                        601)),
                    Task.Run(() => AttemptSendAsync(
                        CreateSendHandler(
                            secondContext,
                            new DispatchBarrierStockLedgerWriter(
                                CreateStockLedgerWriter(secondContext),
                                barrier),
                            cacheInvalidator.Object),
                        601)));

                Assert.Single(outcomes, outcome => outcome.Exception is null);
                Assert.Single(outcomes, outcome => outcome.Exception is DbUpdateException);

                await using (var verify = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    var transfer = await verify.StockTransfers.SingleAsync(row => row.Id == 601);
                    Assert.Equal(StockTransfer.StatusInTransit, transfer.Status);
                    Assert.Equal(101, transfer.DispatchedBy);
                    Assert.NotNull(transfer.DispatchedAt);
                    Assert.Equal(
                        8,
                        await verify.Inventories
                            .Where(row => row.VariantId == 301
                                && row.LocationId == 11
                                && row.LocationType == "outlet")
                            .Select(row => row.Quantity)
                            .SingleAsync());
                    var ledger = await verify.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                            && row.ReferenceId == 601
                            && row.TransactionType == StockLedgerTransactionType.TransferOut)
                        .ToArrayAsync();
                    var movement = Assert.Single(ledger);
                    Assert.Equal(2, movement.QtyOut);
                    Assert.Equal(8, movement.BalanceAfter);
                    Assert.Equal(10m, movement.UnitCost);
                    Assert.Equal(20m, movement.MovementValue);
                }

                await using (var replayContext = CreateContext(database.ConnectionString))
                {
                    var replayHandler = CreateSendHandler(
                        replayContext,
                        CreateStockLedgerWriter(replayContext),
                        cacheInvalidator.Object);
                    var replay = await Assert.ThrowsAsync<InvalidOperationException>(
                        () => SendAsync(replayHandler, 601));
                    Assert.Equal(
                        "Can only send approved transfers. Current status: in_transit",
                        replay.Message);
                }

                await using (var verify = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    await verify.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE stock_ledgers ADD CONSTRAINT ck_stock_transfer_send_test_failure CHECK (qty_in < 0) NOT VALID");
                }

                await using (var rollbackContext = CreateContext(database.ConnectionString))
                {
                    var rollbackHandler = CreateSendHandler(
                        rollbackContext,
                        CreateStockLedgerWriter(rollbackContext),
                        cacheInvalidator.Object);
                    await Assert.ThrowsAsync<DbUpdateException>(() =>
                        SendAsync(rollbackHandler, 602));
                }

                await using (var final = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    var transfer = await final.StockTransfers.SingleAsync(row => row.Id == 602);
                    Assert.Equal(StockTransfer.StatusApproved, transfer.Status);
                    Assert.Null(transfer.DispatchedAt);
                    Assert.Equal(
                        8,
                        await final.Inventories
                            .Where(row => row.VariantId == 301
                                && row.LocationId == 11
                                && row.LocationType == "outlet")
                            .Select(row => row.Quantity)
                            .SingleAsync());
                    Assert.Empty(await final.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                            && row.ReferenceId == 602)
                        .ToArrayAsync());
                }

                cacheInvalidator.Verify(value => value.InvalidateAsync(
                    301,
                    11,
                    It.IsAny<CancellationToken>()), Times.Once);
            });
    }

    [Fact]
    public async Task ConcurrentRejectionCreatesOneReturn_AndLedgerFailureRollsBackAllEffects()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockTransferDispatch,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    await SeedAsync(seed);
                    foreach (var transferId in new long[] { 601, 602 })
                    {
                        var transfer = await seed.StockTransfers.SingleAsync(row => row.Id == transferId);
                        transfer.Status = StockTransfer.StatusInTransit;
                        transfer.DispatchedBy = 101;
                        transfer.DispatchedAt = DateTime.UtcNow;
                    }

                    await seed.SaveChangesAsync();
                }

                using var barrier = new Barrier(2);
                await using var firstContext = CreateContext(database.ConnectionString);
                await using var secondContext = CreateContext(database.ConnectionString);
                var firstHandler = CreateRejectHandler(
                    firstContext,
                    CreateBarrierScopeProvider(barrier).Object);
                var secondHandler = CreateRejectHandler(
                    secondContext,
                    CreateBarrierScopeProvider(barrier).Object);
                var outcomes = await Task.WhenAll(
                    Task.Run(() => AttemptRejectAsync(firstHandler, 601)),
                    Task.Run(() => AttemptRejectAsync(secondHandler, 601)));

                Assert.Single(outcomes, outcome => outcome.Exception is null);
                var rejectedOutcome = Assert.Single(
                    outcomes,
                    outcome => outcome.Exception is InvalidOperationException);
                Assert.Equal(
                    "This transfer must be In Transit before it can be rejected.",
                    rejectedOutcome.Exception!.Message);

                await using (var verify = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    var transfer = await verify.StockTransfers
                        .Include(row => row.Items)
                        .SingleAsync(row => row.Id == 601);
                    Assert.Equal(StockTransfer.StatusRejected, transfer.Status);
                    Assert.Equal(101, transfer.RejectedBy);
                    Assert.Equal(0, transfer.Items.Single().AcceptedQuantity);
                    Assert.Equal(2, transfer.Items.Single().RejectedQuantity);
                    Assert.Single(await verify.StockTransfers
                        .Where(row => row.TransferType == StockTransfer.TransferTypeReturn
                            && row.FromLocationId == 21
                            && row.ToLocationId == 11)
                        .ToArrayAsync());
                    Assert.Single(await verify.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                            && row.ReferenceId == 601
                            && row.TransactionType == StockLedgerTransactionType.TransferRejection)
                        .ToArrayAsync());
                }

                await using (var verify = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    await verify.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE stock_ledgers ADD CONSTRAINT ck_stock_transfer_reject_test_failure CHECK (qty_in < 0) NOT VALID");
                }

                await using (var rollbackContext = CreateContext(database.ConnectionString))
                {
                    var rollbackHandler = CreateRejectHandler(
                        rollbackContext,
                        CreateScopeProvider().Object);
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => RejectAsync(rollbackHandler, 602));
                }

                await using (var final = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    var transfer = await final.StockTransfers
                        .Include(row => row.Items)
                        .SingleAsync(row => row.Id == 602);
                    Assert.Equal(StockTransfer.StatusInTransit, transfer.Status);
                    Assert.Null(transfer.RejectedAt);
                    Assert.Equal(0, transfer.Items.Single().AcceptedQuantity);
                    Assert.Equal(0, transfer.Items.Single().RejectedQuantity);
                    Assert.Empty(await final.StockTransfers
                        .Where(row => row.TransferType == StockTransfer.TransferTypeReturn
                            && row.Notes!.Contains("TRF-TEST-602"))
                        .ToArrayAsync());
                    Assert.Empty(await final.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                            && row.ReferenceId == 602)
                        .ToArrayAsync());
                }
            });
    }

    [Fact]
    public async Task ConcurrentReceive_ProducesOneReturnAndLedgerEffect_AndLedgerFailureRollsBack()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockTransferDispatch,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    await SeedAsync(seed);
                    await AddReceiveScenarioAsync(seed);
                }

                using var barrier = new Barrier(2);
                await using var firstContext = CreateContext(database.ConnectionString);
                await using var secondContext = CreateContext(database.ConnectionString);
                var cacheInvalidator = new Mock<IStockTransferReceiveCacheInvalidator>(MockBehavior.Strict);
                cacheInvalidator.Setup(value => value.InvalidateAsync(
                        301,
                        12,
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                var firstHandler = CreateReceiveHandler(
                    firstContext,
                    cacheInvalidator.Object,
                    CreateBarrierScopeProvider(barrier).Object);
                var secondHandler = CreateReceiveHandler(
                    secondContext,
                    cacheInvalidator.Object,
                    CreateBarrierScopeProvider(barrier).Object);
                var outcomes = await Task.WhenAll(
                    Task.Run(() => AttemptReceiveAsync(firstHandler, 603)),
                    Task.Run(() => AttemptReceiveAsync(secondHandler, 603)));

                Assert.Single(outcomes, outcome => outcome.Exception is null);
                var duplicate = Assert.Single(
                    outcomes,
                    outcome => outcome.Exception is InvalidOperationException);
                Assert.Equal(
                    "This transfer must be In Transit before it can be received.",
                    duplicate.Exception!.Message);

                await using (var verify = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    var transfer = await verify.StockTransfers
                        .Include(row => row.Items)
                        .SingleAsync(row => row.Id == 603);
                    Assert.Equal(StockTransfer.StatusRejected, transfer.Status);
                    Assert.Equal(101, transfer.ReceivedBy);
                    Assert.NotNull(transfer.ReceivedAt);
                    Assert.Equal(101, transfer.UpdatedBy);
                    Assert.Equal(0, transfer.Items.Single().AcceptedQuantity);
                    Assert.Equal(2, transfer.Items.Single().RejectedQuantity);
                    Assert.Equal(
                        4,
                        await verify.Inventories
                            .Where(row => row.VariantId == 301
                                && row.LocationId == 12
                                && row.LocationType == "outlet")
                            .Select(row => row.Quantity)
                            .SingleAsync());
                    var returnTransfer = await verify.StockTransfers
                        .Include(row => row.Items)
                        .SingleAsync(row => row.TransferType == StockTransfer.TransferTypeReturn
                            && row.Notes!.Contains("TRF-TEST-603"));
                    Assert.Equal(StockTransfer.StatusDraft, returnTransfer.Status);
                    Assert.Equal(2, returnTransfer.Items.Single().Quantity);
                    Assert.Equal(0m, returnTransfer.Items.Single().UnitCost);
                    Assert.Single(await verify.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                            && row.ReferenceId == 603
                            && row.TransactionType == StockLedgerTransactionType.TransferIn)
                        .ToArrayAsync());
                }

                cacheInvalidator.Verify(value => value.InvalidateAsync(
                    301,
                    12,
                    It.IsAny<CancellationToken>()), Times.Once);

                await using (var constraintContext = CreateContext(database.ConnectionString))
                {
                    await constraintContext.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE stock_ledgers ADD CONSTRAINT ck_stock_transfer_receive_test_failure CHECK (qty_in < 0) NOT VALID");
                }

                await using (var rollbackContext = CreateContext(database.ConnectionString))
                {
                    var rollbackHandler = CreateReceiveHandler(
                        rollbackContext,
                        cacheInvalidator.Object,
                        CreateScopeProvider().Object);
                    await Assert.ThrowsAsync<DbUpdateException>(() =>
                        ReceiveAsync(rollbackHandler, 604, acceptedQuantity: 1, rejectedQuantity: 0));
                }

                await using (var final = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    var transfer = await final.StockTransfers
                        .Include(row => row.Items)
                        .SingleAsync(row => row.Id == 604);
                    Assert.Equal(StockTransfer.StatusInTransit, transfer.Status);
                    Assert.Null(transfer.ReceivedAt);
                    Assert.Equal(0, transfer.Items.Single().AcceptedQuantity);
                    Assert.Equal(0, transfer.Items.Single().RejectedQuantity);
                    Assert.Equal(
                        4,
                        await final.Inventories
                            .Where(row => row.VariantId == 301
                                && row.LocationId == 12
                                && row.LocationType == "outlet")
                            .Select(row => row.Quantity)
                            .SingleAsync());
                    Assert.Empty(await final.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                            && row.ReferenceId == 604)
                        .ToArrayAsync());
                    Assert.Empty(await final.StockTransfers
                        .Where(row => row.TransferType == StockTransfer.TransferTypeReturn
                            && row.Notes!.Contains("TRF-TEST-604"))
                        .ToArrayAsync());
                }

                cacheInvalidator.Verify(value => value.InvalidateAsync(
                    301,
                    12,
                    It.IsAny<CancellationToken>()), Times.Once);
            });
    }

    [Fact]
    public async Task ConcurrentCancellation_ProducesOneCancelledTransition()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockTransferDispatch,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                    await SeedAsync(seed);

                using var barrier = new Barrier(2);
                await using var firstContext = CreateContext(database.ConnectionString);
                await using var secondContext = CreateContext(database.ConnectionString);
                var firstHandler = CreateCancellationHandler(
                    firstContext,
                    CreateBarrierScopeProvider(barrier).Object);
                var secondHandler = CreateCancellationHandler(
                    secondContext,
                    CreateBarrierScopeProvider(barrier).Object);
                var outcomes = await Task.WhenAll(
                    Task.Run(() => AttemptCancellationAsync(firstHandler, 601)),
                    Task.Run(() => AttemptCancellationAsync(secondHandler, 601)));

                Assert.Single(outcomes, outcome => outcome.Exception is null);
                var duplicate = Assert.Single(
                    outcomes,
                    outcome => outcome.Exception is InvalidOperationException);
                Assert.Equal("Transfer is already cancelled", duplicate.Exception!.Message);

                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                var transfer = await verify.StockTransfers.SingleAsync(row => row.Id == 601);
                Assert.Equal(StockTransfer.StatusCancelled, transfer.Status);
                Assert.Equal(101, transfer.CancelledBy);
                Assert.Equal(101, transfer.UpdatedBy);
                Assert.NotNull(transfer.CancelledAt);
                Assert.NotNull(transfer.UpdatedAt);
                Assert.Equal(
                    10,
                    await verify.Inventories
                        .Where(row => row.VariantId == 301
                            && row.LocationId == 11
                            && row.LocationType == "outlet")
                        .Select(row => row.Quantity)
                        .SingleAsync());
                Assert.Empty(await verify.StockLedgers
                    .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                        && row.ReferenceId == 601)
                    .ToArrayAsync());
            });
    }

    private static async Task<StockTransferReadModel> SubmitAndDispatchAsync(
        SubmitAndDispatchStockTransferCommandHandler handler,
        long transferId)
        => await handler.Handle(
            new SubmitAndDispatchStockTransferCommand(transferId, 101, AutoApprove: true),
            CancellationToken.None);

    private static async Task<DispatchOutcome> AttemptSubmitAndDispatchAsync(
        SubmitAndDispatchStockTransferCommandHandler handler,
        long transferId)
    {
        try
        {
            return new DispatchOutcome(
                await SubmitAndDispatchAsync(handler, transferId),
                null);
        }
        catch (Exception exception)
        {
            return new DispatchOutcome(null, exception);
        }
    }

    private static SubmitAndDispatchStockTransferCommandHandler CreateHandler(
        RetailPOSDbContext dbContext,
        IStockLedgerWriter stockLedgerWriter,
        IStockTransferDispatchCacheInvalidator cacheInvalidator)
    {
        var scopeProvider = new Mock<IStockTransferReadScopeProvider>(MockBehavior.Strict);
        scopeProvider.Setup(provider => provider.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return new SubmitAndDispatchStockTransferCommandHandler(
            scopeProvider.Object,
            new StockTransferDispatchStore(
                dbContext,
                stockLedgerWriter,
                NullLogger<StockTransferDispatchStore>.Instance),
            new StockTransferReadStore(dbContext),
            cacheInvalidator);
    }

    private static SendStockTransferCommandHandler CreateSendHandler(
        RetailPOSDbContext dbContext,
        IStockLedgerWriter stockLedgerWriter,
        IStockTransferDispatchCacheInvalidator cacheInvalidator)
    {
        var scopeProvider = new Mock<IStockTransferReadScopeProvider>(MockBehavior.Strict);
        scopeProvider.Setup(provider => provider.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return new SendStockTransferCommandHandler(
            scopeProvider.Object,
            new StockTransferDispatchStore(
                dbContext,
                stockLedgerWriter,
                NullLogger<StockTransferDispatchStore>.Instance),
            new StockTransferReadStore(dbContext),
            cacheInvalidator);
    }

    private static RejectStockTransferCommandHandler CreateRejectHandler(
        RetailPOSDbContext dbContext,
        IStockTransferReadScopeProvider scopeProvider) =>
        new(
            scopeProvider,
            new StockTransferRejectionStore(
                dbContext,
                CreateStockLedgerWriter(dbContext),
                NullLogger<StockTransferRejectionStore>.Instance),
            new StockTransferReadStore(dbContext));

    private static ReceiveStockTransferCommandHandler CreateReceiveHandler(
        RetailPOSDbContext dbContext,
        IStockTransferReceiveCacheInvalidator cacheInvalidator,
        IStockTransferReadScopeProvider scopeProvider) =>
        new(
            scopeProvider,
            new StockTransferReceiveStore(
                dbContext,
                CreateStockLedgerWriter(dbContext),
                NullLogger<StockTransferReceiveStore>.Instance),
            cacheInvalidator,
            new StockTransferReadStore(dbContext));

    private static CancelStockTransferCommandHandler CreateCancellationHandler(
        RetailPOSDbContext dbContext,
        IStockTransferReadScopeProvider scopeProvider) =>
        new(
            scopeProvider,
            new StockTransferCancellationStore(
                dbContext,
                NullLogger<StockTransferCancellationStore>.Instance),
            new StockTransferReadStore(dbContext));

    private static Mock<IStockTransferReadScopeProvider> CreateScopeProvider()
    {
        var scopeProvider = new Mock<IStockTransferReadScopeProvider>(MockBehavior.Strict);
        scopeProvider.Setup(provider => provider.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return scopeProvider;
    }

    private static Mock<IStockTransferReadScopeProvider> CreateBarrierScopeProvider(Barrier barrier)
    {
        var scopeProvider = new Mock<IStockTransferReadScopeProvider>(MockBehavior.Strict);
        scopeProvider.Setup(provider => provider.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Timed out waiting for concurrent Stock Transfer rejection.");
                return Task.FromResult(1L);
            });
        return scopeProvider;
    }

    private static Task<StockTransferReadModel> RejectAsync(
        RejectStockTransferCommandHandler handler,
        long transferId) =>
        handler.Handle(
            new RejectStockTransferCommand(transferId, "damaged", 101),
            CancellationToken.None);

    private static Task<StockTransferReadModel> ReceiveAsync(
        ReceiveStockTransferCommandHandler handler,
        long transferId,
        int acceptedQuantity,
        int rejectedQuantity) =>
        handler.Handle(
            new ReceiveStockTransferCommand(
                transferId,
                new StockTransferReceiveInput(
                    [new StockTransferReceiveItemInput(301, acceptedQuantity, rejectedQuantity, "checked")],
                    "received"),
                101),
            CancellationToken.None);

    private static Task<StockTransferReadModel> CancelAsync(
        CancelStockTransferCommandHandler handler,
        long transferId) =>
        handler.Handle(
            new CancelStockTransferCommand(transferId, "requested", 101),
            CancellationToken.None);

    private static async Task<DispatchOutcome> AttemptCancellationAsync(
        CancelStockTransferCommandHandler handler,
        long transferId)
    {
        try
        {
            return new DispatchOutcome(await CancelAsync(handler, transferId), null);
        }
        catch (Exception exception)
        {
            return new DispatchOutcome(null, exception);
        }
    }

    private static async Task<DispatchOutcome> AttemptReceiveAsync(
        ReceiveStockTransferCommandHandler handler,
        long transferId)
    {
        try
        {
            return new DispatchOutcome(
                await ReceiveAsync(handler, transferId, acceptedQuantity: 0, rejectedQuantity: 2),
                null);
        }
        catch (Exception exception)
        {
            return new DispatchOutcome(null, exception);
        }
    }

    private static async Task<DispatchOutcome> AttemptRejectAsync(
        RejectStockTransferCommandHandler handler,
        long transferId)
    {
        try
        {
            return new DispatchOutcome(await RejectAsync(handler, transferId), null);
        }
        catch (Exception exception)
        {
            return new DispatchOutcome(null, exception);
        }
    }

    private static Task<StockTransferReadModel> SendAsync(
        SendStockTransferCommandHandler handler,
        long transferId) =>
        handler.Handle(
            new SendStockTransferCommand(transferId, 101),
            CancellationToken.None);

    private static async Task<DispatchOutcome> AttemptSendAsync(
        SendStockTransferCommandHandler handler,
        long transferId)
    {
        try
        {
            return new DispatchOutcome(await SendAsync(handler, transferId), null);
        }
        catch (Exception exception)
        {
            return new DispatchOutcome(null, exception);
        }
    }

    private static IStockLedgerWriter CreateStockLedgerWriter(RetailPOSDbContext dbContext) =>
        new StockLedgerWriter(dbContext, NullLogger<StockLedgerWriter>.Instance);

    private static Mock<IStockTransferDispatchCacheInvalidator> CreateCacheInvalidator()
    {
        var cacheInvalidator = new Mock<IStockTransferDispatchCacheInvalidator>(MockBehavior.Strict);
        cacheInvalidator.Setup(value => value.InvalidateAsync(
                301,
                11,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return cacheInvalidator;
    }

    private static async Task<RetailPOSDbContext> CreateMigratedContextAsync(string connectionString)
    {
        var dbContext = CreateContext(connectionString);
        return await PostgresTestDatabase.InitializeResourceAsync(
            dbContext, context => context.Database.MigrateAsync());
    }

    private static RetailPOSDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new RetailPOSDbContext(options);
    }

    private static async Task SeedAsync(RetailPOSDbContext dbContext)
    {
        dbContext.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Stock Transfer Dispatch Test Business",
            IsActive = true
        });
        dbContext.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Transfer User",
            Email = "stock-transfer-dispatch@example.test",
            PasswordHash = "test",
            IsActive = true
        });
        dbContext.Categories.Add(new Category
        {
            Id = 201,
            BusinessId = 1,
            Name = "Stock Transfer Dispatch Category"
        });
        dbContext.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Stock Transfer Dispatch Source",
            Address = "Test"
        });
        dbContext.Warehouses.Add(new Warehouse
        {
            Id = 21,
            BusinessId = 1,
            Name = "Stock Transfer Dispatch Destination",
            Address = "Test"
        });
        dbContext.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Stock Transfer Dispatch Product",
            ProductCode = "STD-001",
            CategoryId = 201,
            BasePrice = 10m,
            CostPrice = 10m,
            HasVariants = true,
            Status = ProductStatus.Active
        });
        dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = 301,
            BusinessId = 1,
            ProductId = 300,
            Name = "Stock Transfer Dispatch Variant",
            Sku = "STD-001-DEF",
            Attributes = "{}"
        });
        dbContext.Inventories.Add(new InventoryEntity
        {
            Id = 401,
            VariantId = 301,
            LocationId = 11,
            LocationType = "outlet",
            Quantity = 10
        });
        dbContext.StockTransfers.AddRange(
            CreateDraftTransfer(601, 2),
            CreateDraftTransfer(602, 1));
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddReceiveScenarioAsync(RetailPOSDbContext dbContext)
    {
        dbContext.Outlets.Add(new Outlet
        {
            Id = 12,
            BusinessId = 1,
            Name = "Stock Transfer Receive Destination",
            Address = "Test"
        });
        dbContext.Inventories.Add(new InventoryEntity
        {
            Id = 402,
            VariantId = 301,
            LocationId = 12,
            LocationType = "outlet",
            Quantity = 4
        });
        dbContext.StockTransfers.AddRange(
            CreateInTransitTransfer(603, 2),
            CreateInTransitTransfer(604, 2));
        await dbContext.SaveChangesAsync();
    }

    private static StockTransfer CreateInTransitTransfer(long id, int quantity) =>
        new()
        {
            Id = id,
            TransferNo = $"TRF-TEST-{id}",
            TransferType = StockTransfer.TransferTypeDirect,
            FromLocationId = 11,
            FromLocationType = "outlet",
            ToLocationId = 12,
            ToLocationType = "outlet",
            TransferDate = DateTime.UtcNow.Date,
            Status = StockTransfer.StatusInTransit,
            CreatedBy = 101,
            UpdatedBy = 101,
            DispatchedBy = 101,
            DispatchedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Items =
            [
                new StockTransferItem
                {
                    VariantId = 301,
                    Quantity = quantity,
                    RequestedQuantity = quantity,
                    TransferQuantity = quantity,
                    UnitCost = 10m
                }
            ]
        };

    private static StockTransfer CreateDraftTransfer(long id, int quantity) =>
        new()
        {
            Id = id,
            TransferNo = $"TRF-TEST-{id}",
            TransferType = StockTransfer.TransferTypeDirect,
            FromLocationId = 11,
            FromLocationType = "outlet",
            ToLocationId = 21,
            ToLocationType = "warehouse",
            TransferDate = DateTime.UtcNow.Date,
            Status = StockTransfer.StatusDraft,
            CreatedBy = 101,
            CreatedAt = DateTime.UtcNow,
            Items =
            [
                new StockTransferItem
                {
                    VariantId = 301,
                    Quantity = quantity,
                    RequestedQuantity = quantity,
                    TransferQuantity = quantity,
                    UnitCost = 10m
                }
            ]
        };

    private static string GetPostgresConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "RetailPOS PostgreSQL test connection is not configured.");
    }

    private sealed record DispatchOutcome(
        StockTransferReadModel? Transfer,
        Exception? Exception);

    private sealed class DispatchBarrierStockLedgerWriter(
        IStockLedgerWriter inner,
        Barrier barrier) : IStockLedgerWriter
    {
        public void WriteEntry(
            long variantId,
            long locationId,
            string locationType,
            string transactionType,
            int qtyIn,
            int qtyOut,
            int balanceAfter,
            string referenceType,
            long referenceId,
            string? remarks = null,
            long? createdBy = null,
            DateTime? createdAt = null,
            decimal? unitCost = null,
            decimal? movementValue = null,
            DateTime? businessDate = null,
            long? movementSequence = null,
            long? reversalOfMovementId = null) =>
            inner.WriteEntry(
                variantId,
                locationId,
                locationType,
                transactionType,
                qtyIn,
                qtyOut,
                balanceAfter,
                referenceType,
                referenceId,
                remarks,
                createdBy,
                createdAt,
                unitCost,
                movementValue,
                businessDate,
                movementSequence,
                reversalOfMovementId);

        public void WriteEntryWithValuation(
            long variantId,
            long locationId,
            string locationType,
            string transactionType,
            int qtyIn,
            int qtyOut,
            int balanceAfter,
            string referenceType,
            long referenceId,
            string? remarks = null,
            long? createdBy = null,
            DateTime? createdAt = null,
            decimal? unitCost = null,
            decimal? movementValue = null,
            DateTime? businessDate = null,
            long? movementSequence = null,
            long? reversalOfMovementId = null)
        {
            inner.WriteEntryWithValuation(
                variantId,
                locationId,
                locationType,
                transactionType,
                qtyIn,
                qtyOut,
                balanceAfter,
                referenceType,
                referenceId,
                remarks,
                createdBy,
                createdAt,
                unitCost,
                movementValue,
                businessDate,
                movementSequence,
                reversalOfMovementId);
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Timed out waiting for concurrent Stock Transfer dispatch.");
        }
    }
}
