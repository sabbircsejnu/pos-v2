using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.Sales;

using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

public sealed class SaleVoidCommandTests
{
    [Fact]
    public async Task Handler_RecordsTrimmedReasonActorAndPreviousStatus()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = CreateDbContext(connection);
        SeedSaleRelationships(db);
        db.Sales.Add(new Sale
        {
            Id = 77,
            SaleNumber = "VOID-77",
            OutletId = 11,
            CashierId = 8,
            Status = "completed",
            TotalAmount = 120m
        });
        await db.SaveChangesAsync();

        var auditEvents = new List<AuditEventInput>();
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => auditEvents.Add(input))
            .Returns(Task.CompletedTask);
        var cache = new Mock<IPosCacheService>();
        var handler = CreateHandler(db, audit.Object, cache);

        var result = await handler.Handle(
            new VoidSaleCommand(77, "  policy override  ", 99),
            CancellationToken.None);

        Assert.Equal("voided", result.Status);
        var history = await db.SaleVoids.SingleAsync();
        Assert.Equal(99, history.VoidedByUserId);
        Assert.Equal("completed", history.PreviousStatus);
        Assert.Equal("policy override", history.Reason);
        var auditEvent = Assert.Single(
            auditEvents,
            item => item.PrimaryEntity?.Type == "Sale" &&
                    item.PrimaryEntity?.Id == "77" &&
                    item.ActionType == AuditActionType.Reverse);
        Assert.Equal(99L, auditEvent.Metadata?["actorUserId"]);
        Assert.Contains(
            auditEvent.Entities.Single().Fields,
            field => field.FieldName == "Reason" && Equals(field.NewValue, "policy override"));
    }

    [Fact]
    public async Task Handler_RejectsIneligibleSaleBeforeJournalHistoryAndCacheEffects()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = CreateDbContext(connection);
        SeedSaleRelationships(db);
        db.Sales.Add(new Sale
        {
            Id = 88,
            SaleNumber = "VOID-88",
            OutletId = 11,
            CashierId = 8,
            Status = "refunded",
            TotalAmount = 120m
        });
        await db.SaveChangesAsync();

        var audit = new Mock<IAuditService>();
        var cache = new Mock<IPosCacheService>();
        var handler = CreateHandler(db, audit.Object, cache);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new VoidSaleCommand(88, "retry", 9), CancellationToken.None));

        Assert.Equal("Cannot void a refunded sale", exception.Message);
        Assert.Empty(await db.SaleVoids.ToListAsync());
        audit.Verify(service => service.RecordAsync(
            It.IsAny<AuditEventInput>(),
            It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(service => service.InvalidateStockAsync(
            It.IsAny<long>(),
            It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task Handler_RejectsUnauthorizedOutletBeforeMutationEffects()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = CreateDbContext(connection);
        SeedSaleRelationships(db);
        db.Sales.Add(new Sale
        {
            Id = 89,
            SaleNumber = "VOID-89",
            OutletId = 11,
            CashierId = 8,
            Status = "completed",
            TotalAmount = 120m
        });
        await db.SaveChangesAsync();

        var authorizer = new Mock<ISaleMutationOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeAsync(11, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet access denied."));
        var cache = new Mock<IPosCacheService>();
        var handler = new VoidSaleCommandHandler(
            new SaleVoidStore(db),
            new SaleReadStore(db, TestCompanySettings.Provider()),
            authorizer.Object,
            SaleCreationCommandTestFactory.FinancialPostings(db),
            new SaleVoidStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db,
                NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
            new SaleVoidAuditWriter(Mock.Of<IAuditService>()),
            new SaleVoidCacheInvalidator(cache.Object),
            new SaleVoidLogger(NullLogger<SaleVoidLogger>.Instance),
            new RetailPOS.IntegrationTests.Infrastructure.NoCashRefunds());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(new VoidSaleCommand(89, "retry", 9), CancellationToken.None));

        Assert.Equal("completed", await db.Sales.Where(sale => sale.Id == 89)
            .Select(sale => sale.Status).SingleAsync());
        Assert.Empty(await db.SaleVoids.ToListAsync());
        cache.Verify(service => service.InvalidateStockAsync(
            It.IsAny<long>(),
            It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task Handler_PropagatesCancellationBeforeOpeningUseCase()
    {
        await using var connection = await CreateConnectionAsync();
        await using var db = CreateDbContext(connection);
        var cache = new Mock<IPosCacheService>();
        var handler = CreateHandler(db, Mock.Of<IAuditService>(), cache);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.Handle(new VoidSaleCommand(1, "reason", 9), cancellation.Token));

        Assert.Empty(await db.SaleVoids.ToListAsync());
        cache.Verify(service => service.InvalidateStockAsync(
            It.IsAny<long>(),
            It.IsAny<long>()), Times.Never);
    }

    private static async Task<SqliteConnection> CreateConnectionAsync()
    {
        var connection = new SqliteConnection(
            $"Data Source=sale-void-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False");
        await connection.OpenAsync();
        return connection;
    }

    private static RetailPOSDbContext CreateDbContext(SqliteConnection connection)
    {
        var db = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(connection)
                .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static void SeedSaleRelationships(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Business" });
        db.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Outlet",
            Address = "Address"
        });
        db.Users.Add(new User { Id = 8, BusinessId = 1, Name = "Cashier", Email = "cashier@example.test" });
        db.SaveChanges();
    }

    private static VoidSaleCommandHandler CreateHandler(
        RetailPOSDbContext db,
        IAuditService audit,
        Mock<IPosCacheService> cache)
    {
        cache.Setup(service => service.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>()))
            .Returns(Task.CompletedTask);
        return new VoidSaleCommandHandler(
            new SaleVoidStore(db),
            new SaleReadStore(db, TestCompanySettings.Provider()),
            CreateOutletAuthorizer(),
            SaleCreationCommandTestFactory.FinancialPostings(db),
            new SaleVoidStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db,
                NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
            new SaleVoidAuditWriter(audit),
            new SaleVoidCacheInvalidator(cache.Object),
            new SaleVoidLogger(NullLogger<SaleVoidLogger>.Instance),
            new RetailPOS.IntegrationTests.Infrastructure.NoCashRefunds());
    }

    private static ISaleMutationOutletAuthorizer CreateOutletAuthorizer()
    {
        var authorizer = new Mock<ISaleMutationOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return authorizer.Object;
    }
}
