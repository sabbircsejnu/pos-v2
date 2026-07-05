using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

public class SalesCreateTerminalHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SalesCreateTerminalHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateSale_ReturnsBadRequest_WhenNoActiveTerminalExistsForOutlet()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using var client = CreateSalesClient();

        var response = await client.PostAsJsonAsync("/api/sales", BuildRequest(terminalId: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.NotNull(error);
        Assert.Contains("No active terminal is configured", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_ReturnsBadRequest_WhenTerminalDoesNotBelongToOutlet()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient();

        var response = await client.PostAsJsonAsync("/api/sales", BuildRequest(terminalId: 9999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.NotNull(error);
        Assert.Contains("is not active for outlet", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_Succeeds_AndPersistsTerminal_WhenValidTerminalProvided()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient();

        var response = await client.PostAsJsonAsync("/api/sales", BuildRequest(terminalId: 8001));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadApiEnvelope<SaleDto>(response);
        Assert.NotNull(created);
        Assert.Equal(8001, created!.Data?.TerminalId);
    }

    [Fact]
    public async Task CreateSale_ReturnsBadRequest_WhenBackdatedSalesDateWithoutPermission()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient();

        var request = BuildRequest(terminalId: 8001);
        request.SalesDate = DateTime.UtcNow.AddDays(-1);

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.NotNull(error);
        Assert.Contains("backdated", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_ReturnsBadRequest_WhenSalesDateIsFuture()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient(permissions: "sales.view,sales.create,sales.backdate");

        var request = BuildRequest(terminalId: 8001);
        request.SalesDate = DateTime.UtcNow.AddHours(1);

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.NotNull(error);
        Assert.Contains("future", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_SucceedsWithBackdatedSalesDate_WhenPermissionGranted()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient(permissions: "sales.view,sales.create,sales.backdate");

        var backdated = DateTime.UtcNow.AddDays(-2).Date;
        var request = BuildRequest(terminalId: 8001);
        request.SalesDate = backdated;

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadApiEnvelope<SaleDto>(response);
        Assert.NotNull(created?.Data);
        Assert.Equal(backdated, created!.Data!.SaleDate.Date);
        Assert.True(created.Data.CreatedAt.Date >= backdated);
    }

    private HttpClient CreateSalesClient(string permissions = "sales.view,sales.create")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7001");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Cashier");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync(bool includeActiveTerminal)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Test Business",
            IsActive = true
        });

        db.Outlets.Add(new Outlet
        {
            Id = 5001,
            BusinessId = 1,
            Name = "Main Outlet",
            Address = "Dhaka"
        });

        db.Users.Add(new User
        {
            Id = 7001,
            BusinessId = 1,
            Name = "Cashier One",
            Email = "cashier@example.com",
            PasswordHash = "x",
            OutletId = 5001,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            IsActive = true
        });

        db.Customers.Add(new Customer
        {
            Id = 6001,
            Name = Customer.WalkInCustomerName,
            CustomerCode = Customer.WalkInCustomerCode,
            IsSystem = true,
            IsActive = true
        });

        db.Categories.Add(new Category
        {
            Id = 3001,
            Name = "General",
            Description = "Test Category"
        });

        db.Products.Add(new Product
        {
            Id = 3101,
            Name = "Test Product",
            ProductCode = "P-3101",
            CategoryId = 3001,
            BasePrice = 100m,
            CostPrice = 70m,
            Status = ProductStatus.Active,
            HasVariants = true
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 3201,
            ProductId = 3101,
            Name = "Default",
            Sku = "SKU-3201",
            Barcode = "BC-3201",
            Attributes = "{}",
            PriceAdjustment = 0m
        });

        db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
        {
            Id = 9001,
            VariantId = 3201,
            LocationType = "outlet",
            LocationId = 5001,
            Quantity = 50,
            LowStockThreshold = 5
        });

        if (includeActiveTerminal)
        {
            db.PosTerminals.Add(new PosTerminal
            {
                Id = 8001,
                OutletId = 5001,
                Name = "Counter-1",
                Code = "T-1",
                IsActive = true,
                IsDefault = true
            });
        }

        await db.SaveChangesAsync();
    }

    private static CreateSaleDto BuildRequest(long? terminalId)
    {
        return new CreateSaleDto
        {
            OutletId = 5001,
            TerminalId = terminalId,
            CashierId = 7001,
            PaymentMethod = "cash",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            Items =
            [
                new CreateSaleItemDto
                {
                    VariantId = 3201,
                    Quantity = 2,
                    UnitPrice = 100m,
                    DiscountAmount = 0m
                }
            ]
        };
    }

    private static async Task<ApiEnvelope<T>?> ReadApiEnvelope<T>(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class ApiErrorBody
    {
        public string Message { get; set; } = string.Empty;
    }
}
