using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.Grn;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.Grns.Commands;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingConfigurations;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Grns;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.SupplierPayments;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.Tests.Accounting;

namespace RetailPOS.Tests.Purchase;

public sealed class GrnCompletionRelationalTests
{
    static GrnCompletionRelationalTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    private static GrnReadScope BusinessScope(long businessId) => new(businessId, true, [], []);

    private static GrnSearchCriteria LegacySearch(
        long? purchaseOrderId,
        string? status,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize) =>
        new(purchaseOrderId, status, startDate, endDate, null, null, null, pageNumber, pageSize);

    [Fact]
    public async Task Search_PreservesInclusiveDatesOrderingPagingAndBusinessScopedProjection()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db);
                db.Grns.AddRange(
                    new Grn
                    {
                        Id = 42,
                        PoId = 20,
                        ReceivedDate = new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc),
                        Status = "partial"
                    },
                    new Grn
                    {
                        Id = 43,
                        PoId = 20,
                        ReceivedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                        Status = "full"
                    });
                db.Businesses.Add(new Business { Id = 2, Name = "Other Business", IsActive = true });
                db.Suppliers.Add(new Supplier { Id = 11, BusinessId = 2, Name = "Other Supplier" });
                db.Warehouses.Add(new Warehouse { Id = 31, BusinessId = 2, Name = "Other Warehouse" });
                db.PurchaseOrders.Add(new PurchaseOrder
                {
                    Id = 22,
                    PoNumber = "PO-000022",
                    SupplierId = 11,
                    WarehouseId = 31,
                    OrderDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                    Status = "approved"
                });
                db.Grns.Add(new Grn
                {
                    Id = 50,
                    PoId = 22,
                    ReceivedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                    Status = "full"
                });
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                var store = new GrnReadStore(db);
                var firstPage = await store.SearchAsync(
                    LegacySearch(20, null, new DateTime(2026, 3, 10), new DateTime(2026, 3, 11), 1, 1),
                    BusinessScope(1),
                    CancellationToken.None);
                var secondPage = await store.SearchAsync(
                    LegacySearch(20, null, new DateTime(2026, 3, 10), new DateTime(2026, 3, 11), 2, 1),
                    BusinessScope(1),
                    CancellationToken.None);
                var statusFiltered = await store.SearchAsync(
                    LegacySearch(null, "FULL", null, null, 1, 10),
                    BusinessScope(1),
                    CancellationToken.None);

                Assert.Equal(2, firstPage.TotalCount);
                Assert.Equal(1, firstPage.PageNumber);
                Assert.Equal(1, firstPage.PageSize);
                var latest = Assert.Single(firstPage.Grns);
                Assert.Equal(42, latest.Id);
                Assert.Equal("GRN Test Supplier", latest.SupplierName);
                Assert.Equal("GRN Test Warehouse", latest.WarehouseName);
                var item = Assert.Single(Assert.Single(secondPage.Grns).Items);
                Assert.Equal(40, secondPage.Grns[0].Id);
                Assert.Equal(0, item.VariantId);
                Assert.Equal(0, item.OrderedQuantity);
                Assert.Equal(8m, item.UnitCost);
                Assert.Equal([43, 40], statusFiltered.Grns.Select(grn => grn.Id));
                Assert.Equal(2, statusFiltered.TotalCount);
            });
    }

    [Fact]
    public async Task PendingReceiptPos_PreserveLegacyProjectionOrderAndBusinessScope()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db);
                db.Businesses.Add(new Business { Id = 2, Name = "Other Business", IsActive = true });
                db.Suppliers.Add(new Supplier { Id = 11, BusinessId = 2, Name = "Other Supplier" });
                db.Warehouses.Add(new Warehouse { Id = 31, BusinessId = 2, Name = "Other Warehouse" });
                db.PurchaseOrders.AddRange(
                    new PurchaseOrder
                    {
                        Id = 22,
                        PoNumber = "PO-000022",
                        SupplierId = 11,
                        WarehouseId = 31,
                        OrderDate = new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc),
                        Status = "approved"
                    },
                    new PurchaseOrder
                    {
                        Id = 23,
                        PoNumber = "PO-000023",
                        SupplierId = 10,
                        WarehouseId = 30,
                        OrderDate = new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc),
                        Status = "partial",
                        Items =
                        [
                            new PurchaseOrderItem
                            {
                                Id = 24,
                                VariantId = 1,
                                Quantity = 7,
                                UnitPrice = 12m
                            }
                        ]
                    },
                    new PurchaseOrder
                    {
                        Id = 25,
                        PoNumber = "PO-000025",
                        SupplierId = 10,
                        WarehouseId = 30,
                        OrderDate = new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc),
                        Status = "draft"
                    });
                db.ProductImages.AddRange(
                    new ProductImage
                    {
                        Id = 1,
                        ProductId = 1,
                        OriginalPath = "original.jpg",
                        MediumPath = "medium.jpg",
                        ThumbPath = "primary-thumb.jpg",
                        IsPrimary = true
                    },
                    new ProductImage
                    {
                        Id = 2,
                        ProductId = 1,
                        OriginalPath = "other.jpg",
                        MediumPath = "other-medium.jpg",
                        ThumbPath = "other-thumb.jpg",
                        IsPrimary = false
                    });
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                var store = new GrnReadStore(db);
                var results = await store.FindPendingAsync(BusinessScope(1), CancellationToken.None);

                Assert.Equal([23, 20], results.Select(order => order.Id));
                Assert.DoesNotContain(results, order => order.Id == 25);
                var partialOrder = Assert.Single(results, order => order.Id == 23);
                Assert.Equal("partial", partialOrder.Status);
                Assert.Equal("GRN Test Supplier", partialOrder.SupplierName);
                Assert.Equal("GRN Test Warehouse", partialOrder.WarehouseName);
                var item = Assert.Single(partialOrder.Items);
                Assert.Equal(7, item.Quantity);
                Assert.Equal(12m, item.UnitPrice);
                Assert.Equal("GRN Test Product", item.ProductName);
                Assert.Equal("primary-thumb.jpg", item.PrimaryImageThumb);
                Assert.Equal("approved", results.Single(order => order.Id == 20).Status);
            });
    }

    [Fact]
    public async Task VarianceReadStore_PreservesProjectionAndScopesByBusiness()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db);
                db.Businesses.Add(new Business { Id = 2, Name = "Other Business", IsActive = true });
                db.Suppliers.Add(new Supplier { Id = 11, BusinessId = 2, Name = "Other Supplier" });
                db.Warehouses.Add(new Warehouse { Id = 31, BusinessId = 2, Name = "Other Warehouse" });
                db.PurchaseOrders.Add(new PurchaseOrder
                {
                    Id = 22,
                    PoNumber = "PO-000022",
                    SupplierId = 11,
                    WarehouseId = 31,
                    OrderDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                    Status = "approved"
                });
                db.Grns.Add(new Grn
                {
                    Id = 50,
                    PoId = 22,
                    ReceivedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                    Status = "full"
                });
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                var store = new GrnReadStore(db);

                var result = await store.FindVarianceByIdAsync(40, BusinessScope(1), CancellationToken.None);

                Assert.Equal(40, result!.GrnId);
                Assert.Equal(20, result.PurchaseOrderId);
                Assert.Equal("full", result.Status);
                var item = Assert.Single(result.Items);
                Assert.Equal(21, item.PurchaseOrderItemId);
                Assert.Equal(1, item.VariantId);
                Assert.Equal("GRN Test Product", item.ProductName);
                Assert.Equal("GRN Test Variant", item.VariantName);
                Assert.Equal(5, item.OrderedQuantity);
                Assert.Equal(5, item.ReceivedQuantity);
                Assert.Null(await store.FindVarianceByIdAsync(50, BusinessScope(1), CancellationToken.None));
                Assert.Null(await store.FindVarianceByIdAsync(999, BusinessScope(1), CancellationToken.None));
            });
    }

    [Fact]
    public async Task PoVarianceReadStore_UsesOnlyCompletedReceiptsAndPreservesLegacyTotals()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db);
                db.Grns.AddRange(
                    new Grn
                    {
                        Id = 42,
                        PoId = 20,
                        ReceivedDate = new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc),
                        Status = "completed",
                        Items =
                        [
                            new GrnItem
                            {
                                Id = 43,
                                PoItemId = 21,
                                ReceivedQty = 2,
                                UnitCost = 9m
                            }
                        ]
                    },
                    new Grn
                    {
                        Id = 44,
                        PoId = 20,
                        ReceivedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                        Status = "full",
                        Items =
                        [
                            new GrnItem
                            {
                                Id = 45,
                                PoItemId = 21,
                                ReceivedQty = 4,
                                UnitCost = 99m
                            }
                        ]
                    });
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                var store = new GrnPoVarianceReadStore(db);

                var result = await store.FindByPurchaseOrderIdAsync(
                    20,
                    BusinessScope(1),
                    CancellationToken.None);

                Assert.Equal(20, result!.PurchaseOrderId);
                Assert.Equal("approved", result.PurchaseOrderStatus);
                Assert.Equal(3, result.TotalGrns);
                Assert.Equal(1, result.CompletedGrns);
                var item = Assert.Single(result.Items);
                Assert.Equal(21, item.PurchaseOrderItemId);
                Assert.Equal(1, item.VariantId);
                Assert.Equal("GRN Test Product", item.ProductName);
                Assert.Equal("GRN Test Variant", item.VariantName);
                Assert.Equal(5, item.OrderedQuantity);
                Assert.Equal(2, item.TotalReceivedQuantity);
                Assert.Equal(10m, item.UnitPrice);
                Assert.Equal(18m, item.TotalReceivedCost);
                Assert.Null(await store.FindByPurchaseOrderIdAsync(999, BusinessScope(1), CancellationToken.None));
            });
    }

    [Fact]
    public async Task CreateCommand_PersistsCalculatedReceiptAndRecordsActorAudit()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db);
                db.ChangeTracker.Clear();
                var auditEvents = new List<AuditEventInput>();
                var audit = new Mock<IAuditService>();
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) => auditEvents.Add(input))
                    .Returns(Task.CompletedTask);
                var tenantScope = new Mock<IGrnTenantScope>();
                tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(1);
                var handler = new CreateGrnCommandHandler(
                    new GrnCreationStore(db, NullLogger<GrnCreationStore>.Instance),
                    tenantScope.Object,
                    new GrnCreationAuditWriter(audit.Object),
                    Mock.Of<IGrnReceiptLocationAuthorizer>());

                var result = await handler.Handle(
                    new CreateGrnCommand(
                        20,
                        new DateTime(2026, 3, 10, 8, 30, 0),
                        "Dock receipt",
                        "INV-20",
                        new DateTime(2026, 3, 9, 14, 45, 0),
                        [new CreateGrnItemCommand(21, 2, 8m, "Two cartons")],
                        101),
                    CancellationToken.None);

                Assert.Equal(20, result.PurchaseOrderId);
                Assert.Equal("partial", result.Status);
                Assert.Equal("INV-20", result.SupplierInvoiceNumber);
                Assert.Equal(new DateTime(2026, 3, 9), result.SupplierInvoiceDate);
                Assert.Equal(101, result.CreatedBy);
                var item = Assert.Single(result.Items);
                Assert.Equal(2, item.ReceivedQuantity);
                Assert.Equal(8m, item.UnitCost);
                Assert.Equal(16m, item.UnitCost * item.ReceivedQuantity);
                Assert.Equal("Two cartons", item.Notes);
                Assert.Single(auditEvents);
                var auditEvent = Assert.Single(auditEvents);
                Assert.Equal(AuditActionType.Create, auditEvent.ActionType);
                Assert.Equal(AuditModule.Purchase, auditEvent.Module);
                Assert.Equal($"GRN created: {result.Id}", auditEvent.Summary);
                var primaryEntity = auditEvent.PrimaryEntity
                    ?? throw new InvalidOperationException("GRN audit primary entity was not populated.");
                var metadata = auditEvent.Metadata
                    ?? throw new InvalidOperationException("GRN audit metadata was not populated.");
                Assert.Equal("Grn", primaryEntity.Type);
                Assert.Equal(result.Id.ToString(), primaryEntity.Id);
                Assert.Equal(101L, metadata["actorUserId"]);
                Assert.Equal(1L, metadata["businessId"]);
                Assert.Equal(30L, metadata["warehouseId"]);
                Assert.Equal(10L, metadata["supplierId"]);

                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                var persisted = await verify.Grns
                    .Include(grn => grn.Items)
                    .SingleAsync(grn => grn.Id == result.Id);
                Assert.Equal(DateTimeKind.Local, persisted.ReceivedDate.Kind);
                Assert.Equal(16m, persisted.Items.Single().TaxableAmount);
                Assert.Equal(0m, persisted.Items.Single().VatAmount);
            });
    }

    [Fact]
    public async Task CreateCommand_RejectsPurchaseOrderOutsideBusinessWithoutWriting()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db);
                db.Businesses.Add(new Business { Id = 2, Name = "Other Business", IsActive = true });
                db.Suppliers.Add(new Supplier { Id = 11, BusinessId = 2, Name = "Other Supplier" });
                db.Warehouses.Add(new Warehouse { Id = 31, BusinessId = 2, Name = "Other Warehouse" });
                db.PurchaseOrders.Add(new PurchaseOrder
                {
                    Id = 22,
                    PoNumber = "PO-000022",
                    SupplierId = 11,
                    WarehouseId = 31,
                    OrderDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                    Status = "approved",
                    Items = [new PurchaseOrderItem { Id = 24, VariantId = 1, Quantity = 5, UnitPrice = 10m }]
                });
                await db.SaveChangesAsync();
                var tenantScope = new Mock<IGrnTenantScope>();
                tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(1);
                var auditWriter = new Mock<IGrnCreationAuditWriter>();
                var handler = new CreateGrnCommandHandler(
                    new GrnCreationStore(db, NullLogger<GrnCreationStore>.Instance),
                    tenantScope.Object,
                    auditWriter.Object,
                    Mock.Of<IGrnReceiptLocationAuthorizer>());

                await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(
                    new CreateGrnCommand(
                        22,
                        DateTime.UtcNow,
                        null,
                        null,
                        null,
                        [new CreateGrnItemCommand(24, 1, null, null)],
                        101),
                    CancellationToken.None));

                Assert.Equal(1, await db.Grns.CountAsync());
                auditWriter.VerifyNoOtherCalls();
            });
    }

    [Fact]
    public async Task CreateCommand_AuditFailureBubblesAfterReceiptIsPersisted()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db);
                var audit = new Mock<IAuditService>();
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Audit store unavailable"));
                var tenantScope = new Mock<IGrnTenantScope>();
                tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(1);
                var handler = new CreateGrnCommandHandler(
                    new GrnCreationStore(db, NullLogger<GrnCreationStore>.Instance),
                    tenantScope.Object,
                    new GrnCreationAuditWriter(audit.Object),
                    Mock.Of<IGrnReceiptLocationAuthorizer>());

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    handler.Handle(
                        new CreateGrnCommand(
                            20,
                            DateTime.UtcNow,
                            null,
                            null,
                            null,
                            [new CreateGrnItemCommand(21, 1, null, null)],
                            101),
                        CancellationToken.None));

                Assert.Equal("Audit store unavailable", exception.Message);
                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                Assert.Equal(2, await verify.Grns.CountAsync());
                audit.Verify(service => service.RecordAsync(
                    It.IsAny<AuditEventInput>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            });
    }

    [Fact]
    public async Task DetailReadStore_PreservesProjectionAndScopesByBusiness()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db);
                db.Grns.Add(new Grn
                {
                    Id = 42,
                    PoId = 20,
                    ReceivedDate = new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc),
                    Status = "partial"
                });
                db.Businesses.Add(new Business { Id = 2, Name = "Other Business", IsActive = true });
                db.Suppliers.Add(new Supplier { Id = 11, BusinessId = 2, Name = "Other Supplier" });
                db.Warehouses.Add(new Warehouse { Id = 31, BusinessId = 2, Name = "Other Warehouse" });
                db.PurchaseOrders.Add(new PurchaseOrder
                {
                    Id = 22,
                    PoNumber = "PO-000022",
                    SupplierId = 11,
                    WarehouseId = 31,
                    OrderDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                    Status = "approved"
                });
                db.Grns.Add(new Grn
                {
                    Id = 50,
                    PoId = 22,
                    ReceivedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                    Status = "full"
                });
                await db.SaveChangesAsync();
                var store = new GrnReadStore(db);

                var result = await store.FindByIdAsync(40, BusinessScope(1), CancellationToken.None);
                var item = Assert.Single(result!.Items);
                Assert.Equal(40, result.Id);
                Assert.Equal(20, result.PurchaseOrderId);
                Assert.Equal(10, result.SupplierId);
                Assert.Equal("GRN Test Supplier", result.SupplierName);
                Assert.Equal(30, result.WarehouseId);
                Assert.Equal("GRN Test Warehouse", result.WarehouseName);
                Assert.Equal("full", result.Status);
                Assert.Equal(21, item.PurchaseOrderItemId);
                Assert.Equal(1, item.VariantId);
                Assert.Equal("GRN Test Product", item.ProductName);
                Assert.Equal("GRN-TEST-P", item.ProductCode);
                Assert.Equal(5, item.OrderedQuantity);
                Assert.Equal(5, item.ReceivedQuantity);
                Assert.Equal(10m, item.UnitPrice);
                Assert.Equal(8m, item.UnitCost);

                Assert.Null(await store.FindByIdAsync(40, BusinessScope(2), CancellationToken.None));

                var all = await store.FindAllAsync(null, null, BusinessScope(1), CancellationToken.None);
                Assert.Equal([42, 40], all.Select(grn => grn.Id));
                var listItem = Assert.Single(all.Single(grn => grn.Id == 40).Items);
                Assert.Equal(0, listItem.VariantId);
                Assert.Equal(0, listItem.OrderedQuantity);
                Assert.Equal(8m, listItem.UnitCost);
                Assert.Equal(
                    40,
                    Assert.Single(await store.FindAllAsync(20, "FULL", BusinessScope(1), CancellationToken.None)).Id);
                Assert.Equal(
                    42,
                    Assert.Single(await store.FindAllAsync(null, "PARTIAL", BusinessScope(1), CancellationToken.None)).Id);
                Assert.Equal(
                    50,
                    Assert.Single(await store.FindAllAsync(null, null, BusinessScope(2), CancellationToken.None)).Id);
            });
    }

    [Fact]
    public async Task Completion_WritesTheValueTheJournalDebitsToInventory_WhenADiscountApplies()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    await SeedAsync(seed);
                    await InitializeAccountingDefaultsAsync(database.ConnectionString);
                    var orderLine = await seed.PurchaseOrderItems.SingleAsync(item => item.Id == 21);
                    orderLine.Discount = 10m;
                    var receiptLine = await seed.GrnItems.SingleAsync(item => item.Id == 41);
                    receiptLine.TaxableAmount = 36m;
                    await seed.SaveChangesAsync();
                }

                var barrier = new InitialGrnReadBarrier();
                await using var firstContext = await CreateMigratedContextAsync(database.ConnectionString);
                await using var secondContext = await CreateMigratedContextAsync(database.ConnectionString);
                await Task.WhenAll(
                    CompleteAsync(CreateSubject(firstContext, barrier).Handler),
                    CompleteAsync(CreateSubject(secondContext, barrier).Handler));

                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                var movement = await verify.StockLedgers.SingleAsync();
                Assert.True(movement.IsCostValuationReliable);
                Assert.Equal(7.2m, movement.UnitCost);
                Assert.Equal(36m, movement.MovementValue);
                var debit = await verify.JournalEntries
                    .Where(entry => entry.ReferenceType == "Grn" && entry.ReferenceId == "40")
                    .SelectMany(entry => entry.Lines)
                    .Where(line => line.AccountId == 100)
                    .SumAsync(line => line.DebitAmount);
                Assert.Equal(movement.MovementValue, debit);
            });
    }

    [Fact]
    public async Task ConcurrentLegacyCompletions_ApplyEffectsExactlyOnce()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    await SeedAsync(seed);
                    await InitializeAccountingDefaultsAsync(database.ConnectionString);
                }

                var barrier = new InitialGrnReadBarrier();
                await using var firstContext = await CreateMigratedContextAsync(database.ConnectionString);
                await using var secondContext = await CreateMigratedContextAsync(database.ConnectionString);
                var first = CreateSubject(firstContext, barrier);
                var second = CreateSubject(secondContext, barrier);

                var outcomes = await Task.WhenAll(
                    CompleteAsync(first.Handler),
                    CompleteAsync(second.Handler));

                Assert.Equal(2, barrier.InitialReadCount);
                Assert.Single(outcomes, outcome => outcome.Exception is null);
                var concurrencyFailure = Assert.Single(
                    outcomes,
                    outcome => outcome.Exception is DbUpdateConcurrencyException);

                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                Assert.Equal("completed", await verify.Grns
                    .Where(grn => grn.Id == 40)
                    .Select(grn => grn.Status)
                    .SingleAsync());
                Assert.Equal("received", await verify.PurchaseOrders
                    .Where(order => order.Id == 20)
                    .Select(order => order.Status)
                    .SingleAsync());
                Assert.Equal(5, await verify.Inventories
                    .Where(inventory => inventory.VariantId == 1
                        && inventory.LocationId == 30
                        && inventory.LocationType == "warehouse")
                    .Select(inventory => inventory.Quantity)
                    .SingleAsync());
                var journal = await verify.JournalEntries
                    .Include(entry => entry.Lines)
                    .SingleAsync(entry => entry.BusinessId == 1
                        && entry.JournalType == "PurchaseReceiptPosting"
                        && entry.ReferenceType == "Grn"
                        && entry.ReferenceId == "40"
                        && entry.Status == JournalEntryStatuses.Posted);
                Assert.Equal(101, journal.CreatedBy);
                Assert.Equal(101, journal.PostedBy);
                Assert.Equal(2, journal.Lines.Count);
                var payable = await verify.SupplierPayableDocuments
                    .SingleAsync(document => document.SourceDocumentId == 40);
                Assert.Equal(journal.Id, payable.SourceJournalEntryId);
                var ledger = await verify.StockLedgers.SingleAsync();
                Assert.Equal(5, ledger.QtyIn);
                Assert.Equal(8m, ledger.UnitCost);
                Assert.Equal(101, ledger.CreatedBy);

                var completionAudits = first.AuditEvents
                    .Concat(second.AuditEvents)
                    .Where(input => input.ActionType == AuditActionType.Approve
                        && input.PrimaryEntity?.Type == "Grn"
                        && input.PrimaryEntity?.Id == "40")
                    .ToArray();
                Assert.Single(completionAudits);
                Assert.NotNull(concurrencyFailure.Exception);
            });
    }

    private static async Task<CompletionOutcome> CompleteAsync(CompleteGrnCommandHandler handler)
    {
        try
        {
            return new CompletionOutcome(
                await handler.Handle(new CompleteGrnCommand(40, 101), CancellationToken.None),
                null);
        }
        catch (Exception exception)
        {
            return new CompletionOutcome(null, exception);
        }
    }

    private static async Task<RetailPOSDbContext> CreateMigratedContextAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        var db = new RetailPOSDbContext(options);
        return await PostgresTestDatabase.InitializeResourceAsync(
            db, context => context.Database.MigrateAsync());
    }

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "GRN Test Business", IsActive = true });
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "GRN Test Actor",
            Email = "grn-test-actor@example.test",
            PasswordHash = "test",
            IsActive = true
        });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "GRN Test Supplier" });
        db.Warehouses.Add(new Warehouse { Id = 30, BusinessId = 1, Name = "GRN Test Warehouse" });
        db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "GRN Test Category" });
        db.Products.Add(new Product
        {
            Id = 1,
            BusinessId = 1,
            Name = "GRN Test Product",
            ProductCode = "GRN-TEST-P",
            CategoryId = 1,
            CostPrice = 8m
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 1,
            BusinessId = 1,
            ProductId = 1,
            Name = "GRN Test Variant",
            Sku = "GRN-TEST-1"
        });
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 20,
            PoNumber = "PO-000020",
            SupplierId = 10,
            WarehouseId = 30,
            OrderDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = "approved",
            Items =
            [
                new PurchaseOrderItem
                {
                    Id = 21,
                    VariantId = 1,
                    Quantity = 5,
                    UnitPrice = 10m
                }
            ]
        });
        db.Grns.Add(new Grn
        {
            Id = 40,
            PoId = 20,
            ReceivedDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = "full",
            Items =
            [
                new GrnItem
                {
                    Id = 41,
                    PoItemId = 21,
                    ReceivedQty = 5,
                    UnitCost = 8m,
                    TaxableAmount = 40m,
                    VatAmount = 0m
                }
            ]
        });
        db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
        {
            Id = 1,
            VariantId = 1,
            LocationId = 30,
            LocationType = "warehouse",
            Quantity = 0
        });
        db.ChartAccounts.AddRange(
            Account(100, "Inventory", ChartAccountTypes.Asset),
            Account(200, "Payable", ChartAccountTypes.Liability),
            Account(300, "Advance", ChartAccountTypes.Liability));
        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            InventoryAccountId = 100,
            SupplierPayableAccountId = 200,
            SupplierAdvanceAccountId = 300,
            JournalNumberPrefix = "JR",
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear
        });
        db.FinancialYears.Add(new FinancialYear
        {
            Id = 1,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "FY26",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            IsCurrent = true
        });
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 1,
            FinancialYearId = 1,
            PeriodNumber = 3,
            Name = "Mar",
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 31),
            Status = AccountingPeriodStatuses.Open
        });
        await db.SaveChangesAsync();
    }

    private static async Task InitializeAccountingDefaultsAsync(string connectionString)
    {
        await using var db = await CreateMigratedContextAsync(connectionString);
        var provisioner = new AccountingConfigurationProvisioner(
            db,
            NullLogger<AccountingConfigurationProvisioner>.Instance);
        await provisioner.EnsureInitializedAsync(1, null, CancellationToken.None);
    }

    private static GrnCompletionSubject CreateSubject(
        RetailPOSDbContext db,
        InitialGrnReadBarrier barrier)
    {
        var stockLedger = new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
            db,
            NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance);
        var tenant = new Mock<ITenantAccessService>();
        tenant.Setup(service => service.RequireBusinessId()).Returns(1);
        tenant.Setup(service => service.EffectiveBusinessId).Returns(1);
        tenant.Setup(service => service.IsSuperAdmin).Returns(false);
        var journalNumbers = new Mock<IJournalNumberAllocator>();
        var journalSequence = 0;
        journalNumbers
            .Setup(service => service.AllocateNextJournalNumberAsync(
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"JR-{Interlocked.Increment(ref journalSequence):D3}");
        var auditEvents = new ConcurrentBag<AuditEventInput>();
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => auditEvents.Add(input))
            .Returns(Task.CompletedTask);
        var accountingConfiguration = AccountingConfigurationServiceTestFactory.Create(
            db,
            tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        var journalStore = new ManualJournalDraftStore(
            db,
            audit.Object,
            journalNumbers.Object,
            NullLogger<ManualJournalDraftStore>.Instance);
        var accounting = new GrnCompletionAccountingPosterAdapter(
            SupplierPaymentCreationTestHarness.CreateJournalCommandSender(
                journalStore,
                new JournalEntryReadStore(db)),
            accountingConfiguration,
            new SupplierPayableRecognitionWriter(db),
                TestCompanySettings.Provider());
        var auditWriter = new GrnCompletionAuditWriterAdapter(audit.Object);
        var store = new GrnCompletionStore(
            db,
            new GrnCompletionStockLedgerWriterAdapter(stockLedger),
            accounting,
            auditWriter,
            NullLogger<GrnCompletionStore>.Instance);
        var authorizer = new Mock<IGrnCompletionAuthorizer>();
        authorizer.Setup(service => service.AuthorizeAsync(1, 30, "warehouse", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new CompleteGrnCommandHandler(
            new InitialScopeBarrierStore(store, barrier),
            authorizer.Object);
        return new GrnCompletionSubject(handler, auditEvents);
    }

    private static ChartAccount Account(long id, string name, string type) => new()
    {
        Id = id,
        BusinessId = 1,
        AccountCode = id.ToString(),
        AccountName = name,
        AccountType = type,
        NormalBalanceType = type == ChartAccountTypes.Asset
            ? NormalBalanceTypes.Debit
            : NormalBalanceTypes.Credit,
        IsActive = true
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
            ?? throw new InvalidOperationException("RetailPOS PostgreSQL test connection is not configured.");
    }

    private sealed record CompletionOutcome(GrnReadModel? Result, Exception? Exception);

    private sealed record GrnCompletionSubject(
        CompleteGrnCommandHandler Handler,
        ConcurrentBag<AuditEventInput> AuditEvents);

    private sealed class InitialScopeBarrierStore(
        IGrnCompletionStore inner,
        InitialGrnReadBarrier barrier) : IGrnCompletionStore
    {
        public async Task<GrnCompletionScopeReadModel?> FindScopeAsync(
            long grnId,
            CancellationToken cancellationToken)
        {
            var scope = await inner.FindScopeAsync(grnId, cancellationToken);
            if (barrier.ShouldWaitForRead())
                await barrier.WaitAsync();
            return scope;
        }

        public Task<GrnReadModel> CompleteAsync(
            long grnId,
            long actorUserId,
            long? businessId,
            CancellationToken cancellationToken) =>
            inner.CompleteAsync(grnId, actorUserId, businessId, cancellationToken);
    }

    private sealed class InitialGrnReadBarrier
    {
        private readonly TaskCompletionSource _bothReadsReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _repositoryReadCount;
        private int _initialReadCount;

        public int InitialReadCount => Volatile.Read(ref _initialReadCount);

        public bool ShouldWaitForRead() => Interlocked.Increment(ref _repositoryReadCount) <= 2;

        public async Task WaitAsync()
        {
            if (Interlocked.Increment(ref _initialReadCount) == 2)
                _bothReadsReached.TrySetResult();
            await _bothReadsReached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }
}
