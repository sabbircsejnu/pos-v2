using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Purchase;

public sealed class PurchaseAndReceiveHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task PurchaseAndReceive_PreservesRouteAuthorizationResponseAndDuplicateMessage()
    {
        var journal = new Mock<IManualJournalDraftStore>();
        journal
            .Setup(service => service.CreateOrGetSystemDraftAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<ManualJournalDraftInput>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(900);
        journal
            .Setup(service => service.PostAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(900);
        var accounting = new Mock<IAccountingConfigurationService>();
        accounting
            .Setup(service => service.ResolvePostingAccountAsync(
                It.IsAny<string>(),
                It.IsAny<long?>()))
            .ReturnsAsync((string slot, long? _) => new PostingAccountResolutionDto
            {
                Slot = slot,
                AccountId = slot == PostingAccountSlots.Inventory ? 1 : 2,
                Source = "Test"
            });
        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IManualJournalDraftStore>();
                services.AddScoped<IManualJournalDraftStore>(_ => journal.Object);
                services.RemoveAll<IAccountingConfigurationService>();
                services.AddScoped<IAccountingConfigurationService>(_ => accounting.Object);
            }));
        await ResetAndSeedAsync(app.Services);

        using var deniedClient = CreateClient(
            app.CreateClient(),
            "purchases.view,purchases.create");
        var forbidden = await deniedClient.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildRequest("protected-key"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var authorizedClient = CreateClient(
            app.CreateClient(),
            "purchases.view,purchases.create,purchases.receive");
        var response = await authorizedClient.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildRequest("  http-receive  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var responseDocument = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        var root = responseDocument.RootElement;
        Assert.Equal(
            "Purchase order created, auto-GRN generated, and stock updated successfully",
            root.GetProperty("message").GetString());
        var firstData = root.GetProperty("data");
        Assert.False(firstData.GetProperty("isDuplicateRequest").GetBoolean());
        Assert.Equal("completed", firstData.GetProperty("purchaseOrder").GetProperty("status").GetString());
        var firstGrnId = firstData.GetProperty("grnId").GetInt64();
        Assert.StartsWith("GRN-", firstData.GetProperty("grnNumber").GetString());

        var duplicateResponse = await authorizedClient.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildRequest("http-receive", includeItems: false));
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        using var duplicateDocument = await JsonDocument.ParseAsync(
            await duplicateResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Duplicate request detected; existing purchase receive result returned",
            duplicateDocument.RootElement.GetProperty("message").GetString());
        var duplicateData = duplicateDocument.RootElement.GetProperty("data");
        Assert.True(duplicateData.GetProperty("isDuplicateRequest").GetBoolean());
        Assert.Equal(firstGrnId, duplicateData.GetProperty("grnId").GetInt64());

        var failedResponse = await authorizedClient.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildRequest("empty-items", includeItems: false));
        Assert.Equal(HttpStatusCode.BadRequest, failedResponse.StatusCode);
        using (var failedDocument = await JsonDocument.ParseAsync(
                   await failedResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(
                "Purchase order must have at least one item",
                failedDocument.RootElement.GetProperty("message").GetString());
        }

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Single(await db.PurchaseOrders.ToListAsync());
            Assert.Single(await db.Grns.ToListAsync());
            Assert.Equal(3, await db.Inventories.Select(item => item.Quantity).SingleAsync());
            Assert.Single(await db.StockLedgers.ToListAsync());
            var auditEvents = await db.AuditEvents
                .AsNoTracking()
                .Where(audit => audit.ActionSummary == "Purchase & Receive Immediate")
                .OrderBy(audit => audit.Id)
                .ToListAsync();
            Assert.Equal(3, auditEvents.Count);
            Assert.Equal(2, auditEvents.Count(audit => audit.Status == AuditStatus.Success));
            var failure = Assert.Single(auditEvents, audit => audit.Status == AuditStatus.Failed);
            Assert.Equal("Purchase order must have at least one item", failure.ErrorMessage);
        }

        journal.Verify(
            service => service.CreateOrGetSystemDraftAsync(
                1,
                It.IsAny<long>(),
                It.IsAny<ManualJournalDraftInput>(),
                It.Is<string?>(key => key != null && key.StartsWith("grn:")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        journal.Verify(
            service => service.PostAsync(
                1,
                321,
                900,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static HttpClient CreateClient(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "321");
        client.DefaultRequestHeaders.Add("X-Test-Role", "PurchaseManager");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static CreatePurchaseAndReceiveDto BuildRequest(
        string? idempotencyKey,
        bool includeItems = true) =>
        new()
        {
            SupplierId = 1001,
            WarehouseId = 1101,
            OrderDate = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            Notes = "HTTP immediate receive",
            IdempotencyKey = idempotencyKey,
            Items = includeItems
                ? [new CreatePurchaseOrderItemDto
                {
                    VariantId = 1301,
                    Quantity = 3,
                    UnitPrice = 12.5m,
                    Discount = 10m,
                    Tax = 5m,
                    Unit = "pcs"
                }]
                : []
        };

    private static async Task ResetAndSeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "HTTP Purchase Business",
            IsActive = true
        });
        db.Categories.Add(new Category { Id = 1201, BusinessId = 1, Name = "HTTP Purchase Category" });
        db.Products.Add(new Product
        {
            Id = 1251,
            BusinessId = 1,
            Name = "HTTP Purchase Product",
            ProductCode = "HTTP-PURCHASE",
            CategoryId = 1201,
            CostPrice = 10m,
            Status = ProductStatus.Active,
            HasVariants = true
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 1301,
            BusinessId = 1,
            ProductId = 1251,
            Name = "HTTP Purchase Variant",
            Sku = "HTTP-PURCHASE-1"
        });
        db.Suppliers.Add(new Supplier
        {
            Id = 1001,
            BusinessId = 1,
            Name = "HTTP Purchase Supplier"
        });
        db.Users.Add(new User
        {
            Id = 321,
            BusinessId = 1,
            Name = "Purchase Manager",
            Email = "purchase.manager@example.test",
            PasswordHash = "test",
            InventoryLocationAccessScope = User.InventoryAccessAll
        });
        db.Warehouses.Add(new Warehouse
        {
            Id = 1101,
            BusinessId = 1,
            Name = "HTTP Purchase Warehouse"
        });
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment { Id = 1, UserId = 321, WarehouseId = 1101, BusinessId = 1 });
        await db.SaveChangesAsync();
    }
}
