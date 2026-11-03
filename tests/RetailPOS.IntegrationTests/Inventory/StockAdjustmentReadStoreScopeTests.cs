using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;

namespace RetailPOS.Tests.Inventory;

public sealed class StockAdjustmentReadStoreScopeTests : IAsyncLifetime
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
    public async Task AdjustmentQueries_RestrictResultsToBusinessAndAuthorizedLocations()
    {
        var firstBusiness = new Business { Id = 4101, Name = "First business" };
        var secondBusiness = new Business { Id = 4201, Name = "Second business" };
        var authorizedOutlet = new Outlet { Id = 5101, Business = firstBusiness, Name = "Authorized outlet" };
        var unauthorizedOutlet = new Outlet { Id = 5102, Business = firstBusiness, Name = "Unauthorized outlet" };
        var foreignOutlet = new Outlet { Id = 5201, Business = secondBusiness, Name = "Foreign outlet" };
        var authorizedWarehouse = new Warehouse
        {
            Id = 5301,
            Business = firstBusiness,
            Name = "Authorized warehouse"
        };
        var user = new User
        {
            Id = 6101,
            Business = firstBusiness,
            Name = "Adjustment user",
            Email = "stock-adjustment-reader@test.local",
            PasswordHash = "x"
        };
        var category = new Category { Id = 7101, BusinessId = firstBusiness.Id, Name = "Test category" };
        var product = new Product
        {
            Id = 8101,
            BusinessId = firstBusiness.Id,
            Name = "Test product",
            ProductCode = "TEST-PRODUCT",
            Category = category
        };
        var variant = new ProductVariant
        {
            Id = 9101,
            BusinessId = firstBusiness.Id,
            Sku = "TEST-1",
            Barcode = "TEST-BC",
            Product = product
        };

        _dbContext.AddRange(
            firstBusiness,
            secondBusiness,
            authorizedOutlet,
            unauthorizedOutlet,
            foreignOutlet,
            authorizedWarehouse,
            user,
            category,
            product,
            variant);
        await _dbContext.SaveChangesAsync();
        _dbContext.StockAdjustments.AddRange(
            Adjustment(10101, authorizedOutlet.Id, "outlet", user.Id, variant.Id),
            Adjustment(10102, authorizedWarehouse.Id, "warehouse", user.Id, variant.Id),
            Adjustment(10103, unauthorizedOutlet.Id, "outlet", user.Id, variant.Id),
            Adjustment(10104, foreignOutlet.Id, "outlet", user.Id, variant.Id));
        await _dbContext.SaveChangesAsync();

        var scope = new StockAdjustmentReadScope(
            firstBusiness.Id,
            [
                new StockAdjustmentLocationScope(authorizedOutlet.Id, "outlet"),
                new StockAdjustmentLocationScope(authorizedWarehouse.Id, "warehouse")
            ]);
        var store = new StockAdjustmentReadStore(_dbContext);

        var all = await store.GetAllAsync(
            new StockAdjustmentListFilter(),
            scope,
            CancellationToken.None);
        var search = await store.SearchAsync(
            new StockAdjustmentSearchFilter(
                null,
                null,
                variant.Id,
                null,
                null,
                null,
                1,
                1),
            scope,
            CancellationToken.None);
        var history = await store.GetHistoryAsync(
            variant.Id,
            authorizedOutlet.Id,
            scope,
            CancellationToken.None);
        var hidden = await store.GetByIdAsync(10103, scope, CancellationToken.None);

        Assert.Equal([10101L, 10102L], all.Select(adjustment => adjustment.Id).Order().ToArray());
        Assert.Equal(2, search.TotalCount);
        Assert.Equal(10101, search.StockAdjustments[0].Id);
        Assert.Equal(1, search.StockAdjustments[0].LineCount);
        Assert.Equal("Authorized outlet", search.StockAdjustments[0].LocationName);
        Assert.Equal("TEST-PRODUCT", search.StockAdjustments[0].Lines[0].ProductCode);
        Assert.Equal("TEST-BC", search.StockAdjustments[0].Lines[0].Barcode);
        Assert.Equal([10101L], history.Select(adjustment => adjustment.Id).ToArray());
        Assert.Null(hidden);
    }

    [Fact]
    public async Task DraftCreation_RollsBackAdjustmentAndLinesWhenLineInsertFails()
    {
        var business = new Business { Id = 4301, Name = "Create business" };
        var outlet = new Outlet { Id = 5301, Business = business, Name = "Create outlet" };
        var user = new User
        {
            Id = 6301,
            Business = business,
            Name = "Create user",
            Email = "stock-adjustment-create@test.local",
            PasswordHash = "x"
        };
        var category = new Category { Id = 7301, BusinessId = business.Id, Name = "Create category" };
        var product = new Product
        {
            Id = 8301,
            BusinessId = business.Id,
            Name = "Create product",
            Category = category
        };
        var variant = new ProductVariant
        {
            Id = 9301,
            BusinessId = business.Id,
            Sku = "CREATE-1",
            Product = product
        };
        _dbContext.AddRange(business, outlet, user, category, product, variant);
        await _dbContext.SaveChangesAsync();
        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER fail_stock_adjustment_line
            BEFORE INSERT ON stock_adjustment_lines
            BEGIN
                SELECT RAISE(ABORT, 'forced stock adjustment line failure');
            END;
            """);

        var context = new StockAdjustmentCreateContext(
            new StockAdjustmentReadScope(
                business.Id,
                [new StockAdjustmentLocationScope(outlet.Id, "outlet")]),
            AllowNegativeStock: false);
        var input = new StockAdjustmentCreateInput(
            SubmitForApproval: false,
            ApproveImmediately: false,
            outlet.Id,
            "outlet",
            user.Id,
            [new StockAdjustmentCreateLine(variant.Id, 2, "Found", null)]);
        var store = new StockAdjustmentCreationStore(_dbContext);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.CreateAsync(input, context, CancellationToken.None));

        Assert.Empty(await _dbContext.StockAdjustments.AsNoTracking().ToListAsync());
        Assert.Empty(await _dbContext.StockAdjustmentLines.AsNoTracking().ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static StockAdjustment Adjustment(
        long id,
        long locationId,
        string locationType,
        long adjustedBy,
        long variantId)
    {
        var adjustment = new StockAdjustment
        {
            Id = id,
            AdjustmentNumber = $"SA-{id}",
            Status = StockAdjustment.StatusDraft,
            LocationId = locationId,
            LocationType = locationType,
            AdjustedBy = adjustedBy,
            AdjustmentDate = new DateTime(2026, 10, 5),
            CreatedAt = new DateTime(2026, 10, 5),
            UpdatedAt = new DateTime(2026, 10, 5)
        };
        adjustment.Lines.Add(new StockAdjustmentLine
        {
            Id = id + 10000,
            VariantId = variantId,
            PreviousQuantity = 8,
            QuantityChange = 2,
            NewQuantity = 10,
            Reason = "Count correction",
            UnitCostSnapshot = 5m
        });
        return adjustment;
    }
}
