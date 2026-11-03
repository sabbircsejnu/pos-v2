using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public sealed class InventoryLocationAuthorizationPostgresHttpTests
    : IClassFixture<InventoryLocationPostgresFixture>
{
    private readonly InventoryLocationPostgresFixture _fixture;

    public InventoryLocationAuthorizationPostgresHttpTests(InventoryLocationPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AssignedOutletAndWarehouseReads_ExecuteScopedPostgresQueries()
    {
        _fixture.Sql.Clear();
        using var client = _fixture.CreateClient();

        using var outletResponse = await client.GetAsync("/api/inventory/outlet/5001");
        using var warehouseResponse = await client.GetAsync("/api/inventory/warehouse/2001");

        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, warehouseResponse.StatusCode);
        using var outlet = JsonDocument.Parse(await outletResponse.Content.ReadAsStringAsync());
        using var warehouse = JsonDocument.Parse(await warehouseResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, Assert.Single(outlet.RootElement.GetProperty("data").EnumerateArray())
            .GetProperty("id").GetInt64());
        Assert.Equal(3, Assert.Single(warehouse.RootElement.GetProperty("data").EnumerateArray())
            .GetProperty("id").GetInt64());

        var inventorySql = _fixture.Sql.InventoryQueries();
        Assert.Equal(2, inventorySql.Count);
        Assert.Contains(inventorySql, command =>
            command.ContainsLocationPredicate(5001) && !command.ContainsLocationPredicate(2001));
        Assert.Contains(inventorySql, command =>
            command.ContainsLocationPredicate(2001) && !command.ContainsLocationPredicate(5001));
        Assert.All(inventorySql, command =>
            AssertSqlExcludesLocationIds(command, 5002, 5003, 6001, 2002, 6002));
    }

    [Fact]
    public async Task UnauthorizedExplicitLocationsAndFilters_AreDeniedBeforeInventorySql()
    {
        using var client = _fixture.CreateClient();
        var deniedRequests = new (string Method, string Path, object? Body)[]
        {
            ("GET", "/api/inventory/outlet/5003", null),
            ("GET", "/api/inventory/warehouse/2002", null),
            ("GET", "/api/inventory/outlet/6001", null),
            ("GET", "/api/inventory/warehouse/6002", null),
            ("GET", "/api/inventory/outlet/2001", null),
            ("GET", "/api/inventory/warehouse/5001", null),
            ("GET", "/api/inventory/outlet/999999", null),
            ("GET", "/api/inventory/warehouse/999999", null),
            ("GET", "/api/inventory/low-stock?outletId=5003", null),
            ("GET", "/api/inventory/out-of-stock?warehouseId=6002", null),
            ("GET", "/api/inventory/expiring-soon?outletId=6001", null),
            ("GET", "/api/inventory/valuation?warehouseId=2002", null),
            ("POST", "/api/inventory/search", new { outletId = 6001 }),
            ("GET", "/api/inventory/variant-attribute-filters?warehouseId=6002", null)
        };

        foreach (var (method, path, body) in deniedRequests)
        {
            _fixture.Sql.Clear();
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (body is not null)
                request.Content = JsonContent.Create(body);

            using var response = await client.SendAsync(request);

                Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                    $"{method} {path} returned {(int)response.StatusCode}, expected 403.");
                Assert.Empty(_fixture.Sql.InventoryQueries());
        }
    }

    [Fact]
    public async Task OmittedFilters_ReturnOnlyAuthorizedPostgresRowsAndAttributes()
    {
        _fixture.Sql.Clear();
        using var client = _fixture.CreateClient();

        using var lowStockResponse = await client.GetAsync("/api/inventory/low-stock");
        using var outOfStockResponse = await client.GetAsync("/api/inventory/out-of-stock");
        using var expiringResponse = await client.GetAsync("/api/inventory/expiring-soon");
        using var valuationResponse = await client.GetAsync("/api/inventory/valuation");
        using var searchResponse = await client.PostAsJsonAsync(
            "/api/inventory/search", new { page = 1, pageSize = 20 });
        using var filtersResponse = await client.GetAsync("/api/inventory/variant-attribute-filters");

        Assert.Equal(HttpStatusCode.OK, lowStockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, outOfStockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, expiringResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valuationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, filtersResponse.StatusCode);

        var lowStockJson = await lowStockResponse.Content.ReadAsStringAsync();
        var outOfStockJson = await outOfStockResponse.Content.ReadAsStringAsync();
        var expiringJson = await expiringResponse.Content.ReadAsStringAsync();
        var valuationJson = await valuationResponse.Content.ReadAsStringAsync();
        var searchJson = await searchResponse.Content.ReadAsStringAsync();
        var filtersJson = await filtersResponse.Content.ReadAsStringAsync();
        using var lowStock = JsonDocument.Parse(lowStockJson);
        using var outOfStock = JsonDocument.Parse(outOfStockJson);
        using var expiring = JsonDocument.Parse(expiringJson);
        using var valuation = JsonDocument.Parse(valuationJson);
        using var search = JsonDocument.Parse(searchJson);
        using var filters = JsonDocument.Parse(filtersJson);

        Assert.Equal(new long[] { 1, 2, 3 }, lowStock.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("inventoryId").GetInt64()).Order());
        Assert.Equal(new long[] { 1 }, outOfStock.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).Order());
        Assert.Equal(new long[] { 2, 3 }, expiring.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).Order());
        Assert.Equal(3, valuation.RootElement.GetProperty("data").GetProperty("totalItems").GetInt32());
        Assert.Equal(5, valuation.RootElement.GetProperty("data").GetProperty("totalQuantity").GetInt32());
        Assert.Equal(new long[] { 1, 2, 3 }, search.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).Order());
        Assert.DoesNotContain("SECRET-", string.Join('\n',
            lowStockJson, outOfStockJson, expiringJson, valuationJson, searchJson, filtersJson));

        var inventorySql = _fixture.Sql.InventoryQueries();
        Assert.NotEmpty(inventorySql);
        Assert.All(inventorySql, command =>
        {
            Assert.Contains("location_id", command.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(command.NumericParameters(), value => value is 5001 or 5002 or 2001);
            AssertSqlExcludesLocationIds(command, 5003, 6001, 2002, 6002);
        });
    }

    [Fact]
    public async Task UserWithoutAssignedLocations_ReceivesEmptyResultsAndZeroValuation()
    {
        _fixture.Sql.Clear();
        using var client = _fixture.CreateClient(userId: 778);

        using var lowStock = await client.GetAsync("/api/inventory/low-stock");
        using var outOfStock = await client.GetAsync("/api/inventory/out-of-stock");
        using var expiring = await client.GetAsync("/api/inventory/expiring-soon");
        using var valuation = await client.GetAsync("/api/inventory/valuation");
        using var search = await client.PostAsJsonAsync("/api/inventory/search", new { page = 1, pageSize = 20 });
        using var filters = await client.GetAsync("/api/inventory/variant-attribute-filters");

        Assert.Equal(HttpStatusCode.OK, lowStock.StatusCode);
        Assert.Equal(HttpStatusCode.OK, outOfStock.StatusCode);
        Assert.Equal(HttpStatusCode.OK, expiring.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valuation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Equal(HttpStatusCode.OK, filters.StatusCode);

        foreach (var response in new[] { lowStock, outOfStock, expiring, search, filters })
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Empty(json.RootElement.GetProperty("data").EnumerateArray());
        }

        using var valuationJson = JsonDocument.Parse(await valuation.Content.ReadAsStringAsync());
        var valuationData = valuationJson.RootElement.GetProperty("data");
        Assert.Equal(0, valuationData.GetProperty("totalItems").GetInt32());
        Assert.Equal(0, valuationData.GetProperty("totalQuantity").GetInt32());
        Assert.DoesNotContain("SECRET-", await valuation.Content.ReadAsStringAsync());
        var inventorySql = _fixture.Sql.InventoryQueries();
        Assert.NotEmpty(inventorySql);
        Assert.All(inventorySql, command => Assert.True(command.HasEmptyLocationScope()));
    }

    [Fact]
    public async Task SuperAdminRequiresValidatedBusinessAndIsScopedToSelectedBusiness()
    {
        using var client = _fixture.CreateClient(userId: 999, role: "Super Admin");
        _fixture.Sql.Clear();
        using var noContext = await client.GetAsync("/api/inventory/valuation");

        Assert.Equal(HttpStatusCode.Forbidden, noContext.StatusCode);
        Assert.Empty(_fixture.Sql.InventoryQueries());

        client.DefaultRequestHeaders.Add("X-Business-Id", "1");
        _fixture.Sql.Clear();
        using var foreignOutlet = await client.GetAsync("/api/inventory/outlet/6001");
        Assert.Equal(HttpStatusCode.Forbidden, foreignOutlet.StatusCode);
        Assert.Empty(_fixture.Sql.InventoryQueries());

        _fixture.Sql.Clear();
        using var scopedRead = await client.GetAsync("/api/inventory/low-stock");
        Assert.Equal(HttpStatusCode.OK, scopedRead.StatusCode);
        using var json = JsonDocument.Parse(await scopedRead.Content.ReadAsStringAsync());
        Assert.Equal(new long[] { 1, 2, 3 }, json.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("inventoryId").GetInt64()).Order());
        Assert.DoesNotContain("SECRET-", await scopedRead.Content.ReadAsStringAsync());
        Assert.All(_fixture.Sql.InventoryQueries(), command =>
            AssertSqlExcludesLocationIds(command, 6001, 6002));
    }

    [Fact]
    public async Task InventoryPoliciesRemainEnforcedForPostgresRequests()
    {
        using var client = _fixture.CreateClient(permissions: "inventory.view");
        _fixture.Sql.Clear();

        using var response = await client.GetAsync("/api/inventory/low-stock");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_fixture.Sql.InventoryQueries());
    }

    private static void AssertSqlExcludesLocationIds(InventorySqlCommand command, params long[] locationIds) =>
        Assert.DoesNotContain(command.NumericParameters(), locationIds.Contains);
}

public sealed class InventoryLocationPostgresFixture : IAsyncLifetime
{
    private PostgresTestDatabase? _database;
    private PostgresA2RetailPosApiFactory? _factory;
    private string? _baseConnectionString;

    internal InventoryLocationSqlCapture Sql { get; } = new();

    public async Task InitializeAsync()
    {
        _baseConnectionString = GetDedicatedConnectionString();
        _database = await PostgresTestDatabase.CreateAsync(
            _baseConnectionString,
            PostgresTestDatabasePrefix.InventoryLocationAuthorization);
        try
        {
            _factory = new PostgresA2RetailPosApiFactory(_database.ConnectionString, Sql);
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.MigrateAsync();
            await SeedAsync(db);
        }
        catch
        {
            if (_factory is not null)
                await _factory.DisposeAsync();
            await _database.DisposeAsync();
            _database = null;
            throw;
        }
    }

    public HttpClient CreateClient(
        long userId = 777,
        string role = "InventoryAdmin",
        string permissions = "inventory.view,low_stock_alerts.view")
    {
        var client = (_factory ?? throw new InvalidOperationException("Fixture is not initialized."))
            .CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private static string GetDedicatedConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "RETAILPOS_TEST_POSTGRES must explicitly target the isolated A2 PostgreSQL test server.");

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(configured);
        if (builder.Host != "127.0.0.1" || builder.Port != 55432
            || builder.Database != "a2_test_guard" || builder.Username != "a2_test_runner"
            || string.IsNullOrWhiteSpace(builder.Password))
            throw new InvalidOperationException(
                "RETAILPOS_TEST_POSTGRES does not match the approved isolated A2 PostgreSQL endpoint.");

        builder.Pooling = false;
        return builder.ConnectionString;
    }

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(
            new Business { Id = 1, Name = "A2 Test Business A" },
            new Business { Id = 2, Name = "A2 Test Business B" });
        db.Outlets.AddRange(
            new Outlet { Id = 5001, BusinessId = 1, Name = "A2 Outlet Assigned" },
            new Outlet { Id = 5002, BusinessId = 1, Name = "A2 Outlet Assigned 2" },
            new Outlet { Id = 5003, BusinessId = 1, Name = "A2 Outlet Unassigned" },
            new Outlet { Id = 6001, BusinessId = 2, Name = "A2 Outlet Foreign" });
        db.Warehouses.AddRange(
            new Warehouse { Id = 2001, BusinessId = 1, Name = "A2 Warehouse Assigned" },
            new Warehouse { Id = 2002, BusinessId = 1, Name = "A2 Warehouse Unassigned" },
            new Warehouse { Id = 6002, BusinessId = 2, Name = "A2 Warehouse Foreign" });
        db.Roles.Add(new Role { Id = 99, Name = "Super Admin", Permissions = "[\"inventory.view\"]" });
        db.Users.AddRange(
            new User
            {
                Id = 777,
                BusinessId = 1,
                OutletId = 5001,
                Name = "Assigned A2 inventory user",
                Email = "a2-inventory-user@example.test",
                PasswordHash = "test-only",
                InventoryLocationAccessScope = User.InventoryAccessSpecific,
                IsActive = true
            },
            new User
            {
                Id = 778,
                BusinessId = 1,
                Name = "Unassigned A2 inventory user",
                Email = "a2-inventory-unassigned@example.test",
                PasswordHash = "test-only",
                InventoryLocationAccessScope = User.InventoryAccessSpecific,
                IsActive = true
            },
            new User
            {
                Id = 999,
                RoleId = 99,
                Name = "A2 test Super Admin",
                Email = "a2-superadmin@example.test",
                PasswordHash = "test-only",
                InventoryLocationAccessScope = User.InventoryAccessAll,
                IsActive = true
            });
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { Id = 1, UserId = 777, OutletId = 5001, BusinessId = 1, IsPrimary = true },
            new UserOutletAssignment { Id = 2, UserId = 777, OutletId = 5002, BusinessId = 1 });
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment
        {
            Id = 1,
            UserId = 777,
            WarehouseId = 2001,
            BusinessId = 1,
            IsPrimary = true
        });
        db.Categories.Add(new Category { Id = 7001, Name = "A2 Authorization Test", BusinessId = 1 });
        db.Products.Add(new Product
        {
            Id = 8001,
            BusinessId = 1,
            Name = "A2 Authorization Test Product",
            ProductCode = "A2-AUTH-001",
            CategoryId = 7001,
            BasePrice = 10m,
            CostPrice = 5m,
            HasVariants = true
        });
        db.Variations.Add(new Variation { Id = 7101, Name = "Color", DisplayOrder = 1, IsActive = true });

        var records = new (long VariantId, long LocationId, string LocationType, string Attribute, int Quantity)[]
        {
            (9001, 5001, "outlet", "A1", 0),
            (9002, 5002, "outlet", "A2", 2),
            (9003, 2001, "warehouse", "W1", 3),
            (9004, 5003, "outlet", "SECRET-BIZ-A-OUTLET", 124),
            (9005, 6001, "outlet", "SECRET-BIZ-B-OUTLET", 125),
            (9006, 2002, "warehouse", "SECRET-BIZ-A-WAREHOUSE", 126),
            (9007, 6002, "warehouse", "SECRET-BIZ-B-WAREHOUSE", 127)
        };
        foreach (var (variantId, _, _, attribute, _) in records)
        {
            db.ProductVariants.Add(new ProductVariant
            {
                Id = variantId,
                BusinessId = 1,
                ProductId = 8001,
                Name = attribute,
                Sku = $"A2-AUTH-{variantId}",
                Barcode = $"A2-BARCODE-{variantId}",
                Attributes = JsonSerializer.Serialize(new { Color = attribute })
            });
            db.VariationOptions.Add(new VariationOption
            {
                Id = 7200 + variantId - 9000,
                VariationId = 7101,
                Name = attribute,
                DisplayOrder = (int)(variantId - 9000),
                IsActive = true
            });
        }

        foreach (var (variantId, locationId, locationType, _, quantity) in records)
        {
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = variantId - 9000,
                VariantId = variantId,
                LocationId = locationId,
                LocationType = locationType,
                Quantity = quantity,
                LowStockThreshold = 10,
                ExpiryDate = DateTime.UtcNow.AddDays(15)
            });
        }

        await db.SaveChangesAsync();
    }
}

internal sealed class PostgresA2RetailPosApiFactory(
    string connectionString,
    InventoryLocationSqlCapture sql) : RetailPosApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
            services.RemoveAll<RetailPOSDbContext>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));
            services.AddDbContext<RetailPOSDbContext>((_, options) =>
                options.UseNpgsql(connectionString).AddInterceptors(sql));
        });
    }
}

internal sealed class InventoryLocationSqlCapture : DbCommandInterceptor
{
    private readonly ConcurrentQueue<InventorySqlCommand> _commands = new();

    public void Clear()
    {
        while (_commands.TryDequeue(out _)) { }
    }

    public IReadOnlyList<InventorySqlCommand> InventoryQueries() =>
        _commands.Where(command =>
            command.CommandText.Contains("FROM inventories", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Capture(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbCommand command)
    {
        var parameters = command.Parameters
            .Cast<DbParameter>()
            .Select(parameter => new KeyValuePair<string, object?>(parameter.ParameterName, parameter.Value))
            .ToArray();
        _commands.Enqueue(new InventorySqlCommand(command.CommandText, parameters));
    }
}

internal sealed record InventorySqlCommand(
    string CommandText,
    IReadOnlyList<KeyValuePair<string, object?>> Parameters)
{
    public IReadOnlyList<long> NumericParameters() =>
        Parameters.SelectMany(parameter => parameter.Value switch
            {
                long value => [value],
                int value => [(long)value],
                IEnumerable<long> values => values,
                IEnumerable<int> values => values.Select(value => (long)value),
                _ => Array.Empty<long>()
            })
            .ToArray();

    public bool ContainsLocationPredicate(long locationId) =>
        NumericParameters().Contains(locationId)
        && CommandText.Contains("location_id", StringComparison.OrdinalIgnoreCase);

    public bool HasEmptyLocationScope()
    {
        var scopeParameters = Parameters
            .Where(parameter => parameter.Key.Contains("locationScope_", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return scopeParameters.Length > 0
            && scopeParameters.All(parameter => parameter.Value is Array { Length: 0 });
    }
}
