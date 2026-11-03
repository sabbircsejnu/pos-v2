using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.ReadStores;

namespace RetailPOS.Tests.Inventory;

public class StockLedgerValuationTests
{
    [Fact]
    public async Task WriteEntry_PersistsWeightedAverageValuationMetadataAndSequence()
    {
        var db = CreateDbContext();
        var service = new StockLedgerWriter(
            db,
            Mock.Of<ILogger<StockLedgerWriter>>());

        var firstCreatedAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var secondCreatedAt = firstCreatedAt.AddHours(1);

        service.WriteEntry(
            variantId: 10,
            locationId: 200,
            locationType: "warehouse",
            transactionType: "grn",
            qtyIn: 10,
            qtyOut: 0,
            balanceAfter: 10,
            referenceType: "grn",
            referenceId: 1,
            remarks: "initial receipt",
            createdBy: 7,
            createdAt: firstCreatedAt,
            unitCost: 10m);

        service.WriteEntry(
            variantId: 10,
            locationId: 200,
            locationType: "warehouse",
            transactionType: "sale",
            qtyIn: 0,
            qtyOut: 4,
            balanceAfter: 6,
            referenceType: "sale",
            referenceId: 2,
            remarks: "sale issue",
            createdBy: 7,
            createdAt: secondCreatedAt,
            unitCost: 10m);

        await db.SaveChangesAsync();

        var rows = await db.StockLedgers
            .OrderBy(x => x.MovementSequence)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1L, rows[0].MovementSequence);
        Assert.Equal(2L, rows[1].MovementSequence);
        Assert.Equal(10m, rows[0].UnitCost);
        Assert.Equal(100m, rows[0].MovementValue);
        Assert.Equal(10m, rows[1].UnitCost);
        Assert.Equal(40m, rows[1].MovementValue);
        Assert.Equal(firstCreatedAt.Date, rows[0].BusinessDate);
        Assert.Equal(secondCreatedAt.Date, rows[1].BusinessDate);
        Assert.True(rows[0].IsCostValuationReliable);
        Assert.True(rows[1].IsCostValuationReliable);
    }

    [Fact]
    public async Task InfrastructureWriter_QueuesValuationEntriesWithoutSavingCallerContext()
    {
        var db = CreateDbContext();
        IStockLedgerWriter writer = new StockLedgerWriter(
            db,
            Mock.Of<ILogger<StockLedgerWriter>>());
        var createdAt = new DateTime(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc);

        writer.WriteEntryWithValuation(
            variantId: 20,
            locationId: 300,
            locationType: " WAREHOUSE ",
            transactionType: "grn",
            qtyIn: 3,
            qtyOut: 0,
            balanceAfter: 3,
            referenceType: "grn",
            referenceId: 1,
            createdAt: createdAt,
            unitCost: 5m);
        writer.WriteEntry(
            variantId: 20,
            locationId: 300,
            locationType: "warehouse",
            transactionType: "legacy_adjustment",
            qtyIn: 0,
            qtyOut: 1,
            balanceAfter: 2,
            referenceType: "adjustment",
            referenceId: 2,
            createdAt: createdAt.AddHours(1));

        var trackedEntries = db.StockLedgers.Local.OrderBy(row => row.MovementSequence).ToArray();
        Assert.Equal(2, trackedEntries.Length);
        Assert.All(db.ChangeTracker.Entries<StockLedger>(), entry => Assert.Equal(
            EntityState.Added,
            entry.State));
        Assert.Equal("warehouse", trackedEntries[0].LocationType);
        Assert.Equal(1L, trackedEntries[0].MovementSequence);
        Assert.Equal(5m, trackedEntries[0].UnitCost);
        Assert.Equal(15m, trackedEntries[0].MovementValue);
        Assert.True(trackedEntries[0].IsCostValuationReliable);
        Assert.Equal(2L, trackedEntries[1].MovementSequence);
        Assert.False(trackedEntries[1].IsCostValuationReliable);
        Assert.Equal(0m, trackedEntries[1].MovementValue);

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.StockLedgers.CountAsync());
    }

    [Fact]
    public async Task WriteEntry_WithMissingCost_MarksMovementAsUnvalued()
    {
        var db = CreateDbContext();
        var service = new StockLedgerWriter(
            db,
            Mock.Of<ILogger<StockLedgerWriter>>());

        service.WriteEntry(
            variantId: 11,
            locationId: 201,
            locationType: "warehouse",
            transactionType: "legacy_adjustment",
            qtyIn: 2,
            qtyOut: 0,
            balanceAfter: 2,
            referenceType: "legacy",
            referenceId: 99,
            remarks: "legacy movement without cost",
            createdBy: 7,
            createdAt: new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc));

        await db.SaveChangesAsync();

        var row = await db.StockLedgers.SingleAsync();
        Assert.False(row.IsCostValuationReliable);
        Assert.Equal(0m, row.UnitCost);
        Assert.Equal(0m, row.MovementValue);
    }

    [Fact]
    public async Task PersistedStockLedgerEntries_AreImmutableAfterSave()
    {
        var db = CreateDbContext();
        var service = new StockLedgerWriter(
            db,
            Mock.Of<ILogger<StockLedgerWriter>>());

        service.WriteEntry(
            variantId: 12,
            locationId: 202,
            locationType: "warehouse",
            transactionType: "grn",
            qtyIn: 5,
            qtyOut: 0,
            balanceAfter: 5,
            referenceType: "grn",
            referenceId: 100,
            unitCost: 30m);

        await db.SaveChangesAsync();

        var row = await db.StockLedgers.SingleAsync();
        row.UnitCost = 99m;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task WriteEntry_AssignsMonotonicMovementSequenceForRepeatedWrites()
    {
        var db = CreateDbContext();
        var service = new StockLedgerWriter(
            db,
            Mock.Of<ILogger<StockLedgerWriter>>());

        for (var i = 0; i < 3; i++)
        {
            service.WriteEntry(
                variantId: 13,
                locationId: 203,
                locationType: "warehouse",
                transactionType: "adjustment",
                qtyIn: 1,
                qtyOut: 0,
                balanceAfter: i + 1,
                referenceType: "legacy",
                referenceId: 200 + i,
                unitCost: 5m);
        }

        await db.SaveChangesAsync();

        var sequences = await db.StockLedgers
            .OrderBy(x => x.MovementSequence)
            .Select(x => x.MovementSequence)
            .ToListAsync();

        Assert.Equal(new[] { 1L, 2L, 3L }, sequences);
    }

    private static RetailPOSDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase($"StockLedgerValuationTests_{Guid.NewGuid()}")
            .Options;

        return new RetailPOSDbContext(options);
    }
}
