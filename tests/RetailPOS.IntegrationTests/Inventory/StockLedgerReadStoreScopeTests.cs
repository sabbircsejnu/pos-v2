using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockLedger;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;

namespace RetailPOS.Tests.Inventory;

public sealed class StockLedgerReadStoreScopeTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private RetailPOSDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new RetailPOSDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task ReferenceAndSearchQueries_RestrictResultsToBusinessAndAuthorizedLocations()
    {
        var firstBusiness = new Business { Id = 4101, Name = "First business" };
        var secondBusiness = new Business { Id = 4201, Name = "Second business" };
        var firstOutlet = new Outlet { Id = 5101, Business = firstBusiness, Name = "Authorized outlet" };
        var secondOutlet = new Outlet { Id = 5102, Business = firstBusiness, Name = "Other outlet" };
        var foreignOutlet = new Outlet { Id = 5201, Business = secondBusiness, Name = "Foreign outlet" };
        var category = new Category { Id = 6101, BusinessId = firstBusiness.Id, Name = "Test category" };
        var product = new Product { Id = 7101, BusinessId = firstBusiness.Id, Name = "Test product", Category = category };
        var variant = new ProductVariant { Id = 8101, BusinessId = firstBusiness.Id, Sku = "TEST-1", Product = product };

        _dbContext.AddRange(
            firstBusiness,
            secondBusiness,
            firstOutlet,
            secondOutlet,
            foreignOutlet,
            category,
            product,
            variant);
        await _dbContext.SaveChangesAsync();
        _dbContext.StockLedgers.AddRange(
            Entry(9101, variant, firstOutlet.Id, "sale", 1),
            Entry(9102, variant, secondOutlet.Id, "sale", 1),
            Entry(9103, variant, foreignOutlet.Id, "sale", 1));
        await _dbContext.SaveChangesAsync();

        var scope = new StockLedgerReadScope(
            firstBusiness.Id,
            [new StockLedgerLocationScope(firstOutlet.Id, "outlet")]);
        var store = new StockLedgerReadStore(_dbContext);

        var byReference = await store.GetByReferenceAsync("SALE", 1, scope, CancellationToken.None);
        var search = await store.SearchAsync(
            new StockLedgerSearchFilter(null, null, null, null, null, null, null, null, 1, 20),
            scope,
            CancellationToken.None);

        var referenceEntry = Assert.Single(byReference);
        Assert.Equal(9101, referenceEntry.Id);
        Assert.Equal("Authorized outlet", referenceEntry.LocationName);
        Assert.Single(search.Entries);
        Assert.Equal(9101, search.Entries[0].Id);
        Assert.Equal(1, search.TotalCount);
    }

    [Fact]
    public async Task WriterAppend_RollsBackWithCallerTransaction()
    {
        var business = new Business { Id = 4301, Name = "Transaction business" };
        var outlet = new Outlet { Id = 5301, Business = business, Name = "Transaction outlet" };
        var category = new Category { Id = 6301, BusinessId = business.Id, Name = "Transaction category" };
        var product = new Product { Id = 7301, BusinessId = business.Id, Name = "Transaction product", Category = category };
        var variant = new ProductVariant { Id = 8301, BusinessId = business.Id, Sku = "TX-1", Product = product };
        _dbContext.AddRange(business, outlet, category, product, variant);
        await _dbContext.SaveChangesAsync();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var writer = new StockLedgerWriter(
            _dbContext,
            NullLogger<StockLedgerWriter>.Instance);
        writer.WriteEntryWithValuation(
            variant.Id,
            outlet.Id,
            "outlet",
            "grn",
            3,
            0,
            3,
            "grn",
            101,
            unitCost: 5m);
        await _dbContext.SaveChangesAsync();
        await transaction.RollbackAsync();

        Assert.Empty(await _dbContext.StockLedgers.AsNoTracking().ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static StockLedger Entry(
        long id,
        ProductVariant variant,
        long locationId,
        string referenceType,
        long referenceId) =>
        new()
        {
            Id = id,
            Variant = variant,
            LocationId = locationId,
            LocationType = "outlet",
            TransactionType = "sale",
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            QtyOut = 1,
            BalanceAfter = 0,
            BusinessDate = new DateTime(2026, 10, 5),
            CreatedAt = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc)
        };
}
