using System.Net;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailPOS.API.Documents.Pdf;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.Tests.Purchase;

public sealed class PurchaseDocumentPdfBoundaryTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task PdfReads_PreserveProjectionFormattingPermissionsCostFlagAndRoutes()
    {
        var renderer = new CapturingRenderer();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDocumentPdfRenderer>();
            services.AddSingleton<IDocumentPdfRenderer>(renderer);
        }));
        await SeedAsync(app.Services);
        using var client = CreateClient(app.CreateClient(), "purchases.view,grn.view,products.view_cost");
        foreach (var route in new[] { "/api/purchase-orders/20/pdf", "/api/documents/po/20/pdf", "/api/documents/grn/40/pdf" })
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync()));
            Assert.Contains(route.Contains("grn", StringComparison.Ordinal) ? "GRN-000040.pdf" : "PO-TEST-20.pdf",
                response.Content.Headers.ContentDisposition?.ToString());
        }
        var po = renderer.Requests[0];
        Assert.Equal(DocumentPdfDocumentType.PurchaseOrder, po.DocumentType);
        Assert.Equal("PO-TEST-20", po.DocumentNumber);
        Assert.Equal("Supplier 1", po.BillTo?.Name);
        Assert.Equal("Warehouse 1", po.ShipTo?.Name);
        Assert.Equal("PO notes", po.Notes);
        Assert.Equal("approved", po.Status);
        Assert.True(po.ShowCostFields);
        var poItem = Assert.Single(po.Items);
        Assert.Equal(new DocumentPdfLineItem(1, "Product", "Blue", "SKU-1", "P-1",
            "Color=Blue", 5, "piece", 10m, 10m, 5m, 47.25m), poItem);
        Assert.Equal(new DocumentPdfSummary(50m, 5m, 2.25m, 47.25m), po.Summary);
        Assert.Contains(po.Signatures!, signature => signature.Value == "Buyer");
        var grn = renderer.Requests[2];
        Assert.Equal("GRN-000040", grn.DocumentNumber);
        Assert.Equal("PO-000020", grn.ReferenceNumber);
        Assert.Equal("Receipt notes", grn.Notes);
        Assert.Equal(new DocumentPdfLineItem(1, "Product", "Blue", null, "P-1",
            "Color=Blue", 4, null, 8m, null, null, 32m), Assert.Single(grn.Items));
        Assert.Equal(new DocumentPdfSummary(32m, 0m, 0m, 32m), grn.Summary);
        Assert.Contains(grn.Signatures!, signature => signature.Value == "Buyer");

        using var noCost = CreateClient(app.CreateClient(), "purchases.view,grn.view");
        Assert.Equal(HttpStatusCode.OK, (await noCost.GetAsync("/api/documents/grn/40/pdf")).StatusCode);
        Assert.False(renderer.Requests[^1].ShowCostFields);
        using var denied = CreateClient(app.CreateClient(), "sales.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync("/api/purchase-orders/20/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync("/api/documents/grn/40/pdf")).StatusCode);
        var swagger = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.Contains("/api/purchase-orders/{id}/pdf", swagger.Paths.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("/api/documents/{documentType}/{documentId}/pdf", swagger.Paths.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PdfReads_PreserveMissingRecordErrorsAndPurchaseOrderBusinessScope()
    {
        await SeedAsync(factory.Services);
        using var client = CreateClient(factory.CreateClient(), "purchases.view,grn.view");
        foreach (var (route, message) in new[]
        {
            ("/api/purchase-orders/999/pdf", "Purchase order with ID 999 not found"),
            ("/api/documents/grn/999/pdf", "GRN with ID 999 not found"),
            ("/api/purchase-orders/21/pdf", "Purchase order with ID 21 not found"),
            ("/api/documents/grn/41/pdf", "GRN with ID 41 not found")
        })
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains(message, await response.Content.ReadAsStringAsync());
        }

        // The export follows the GRN location rules: without a Step 5 assignment the GRN looks missing.
        using var unassigned = CreateClient(factory.CreateClient(), "purchases.view,grn.view", userId: 8);
        var unassignedExport = await unassigned.GetAsync("/api/documents/grn/40/pdf");
        Assert.Equal(HttpStatusCode.NotFound, unassignedExport.StatusCode);
        Assert.Contains("GRN with ID 40 not found", await unassignedExport.Content.ReadAsStringAsync());

        using var superAdmin = factory.CreateClient();
        superAdmin.DefaultRequestHeaders.Add("X-Test-UserId", "7");
        superAdmin.DefaultRequestHeaders.Add("X-Test-Role", SystemRoleNames.SuperAdmin);
        superAdmin.DefaultRequestHeaders.Add("X-Test-Permissions", "purchases.view,grn.view");
        superAdmin.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.GetAsync("/api/documents/grn/41/pdf")).StatusCode);

        // A Super Admin is scoped to the selected Business and never reads another Business's documents.
        using var wrongBusinessSuperAdmin = factory.CreateClient();
        wrongBusinessSuperAdmin.DefaultRequestHeaders.Add("X-Test-UserId", "7");
        wrongBusinessSuperAdmin.DefaultRequestHeaders.Add("X-Test-Role", SystemRoleNames.SuperAdmin);
        wrongBusinessSuperAdmin.DefaultRequestHeaders.Add("X-Test-Permissions", "purchases.view,grn.view");
        wrongBusinessSuperAdmin.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        Assert.Equal(HttpStatusCode.NotFound, (await wrongBusinessSuperAdmin.GetAsync("/api/documents/grn/41/pdf")).StatusCode);
    }

    private static HttpClient CreateClient(HttpClient client, string permissions, long userId = 7)
    {
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-OutletId", "999");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "WarehouseManager");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        PurchaseDocumentSeed.Add(db);
        await db.SaveChangesAsync();
    }

    private sealed class CapturingRenderer : IDocumentPdfRenderer
    {
        public List<DocumentPdfRequest> Requests { get; } = [];
        public Task<byte[]> RenderAsync(DocumentPdfRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new QuestDocumentPdfRenderer().RenderAsync(request, cancellationToken);
        }
    }
}

internal static class PurchaseDocumentSeed
{
    public static void Add(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(new Business { Id = 1, Name = "Business 1" }, new Business { Id = 2, Name = "Business 2" });
        db.Users.AddRange(
            new User { Id = 7, BusinessId = 1, Name = "Buyer", Email = "buyer@example.test", PasswordHash = "test" },
            new User { Id = 8, BusinessId = 1, Name = "Unassigned", Email = "unassigned@example.test", PasswordHash = "test" });
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment { Id = 1, UserId = 7, WarehouseId = 30, BusinessId = 1 });
        db.Suppliers.AddRange(new Supplier { Id = 10, BusinessId = 1, Name = "Supplier 1", Address = "Vendor address" },
            new Supplier { Id = 11, BusinessId = 2, Name = "Supplier 2" });
        db.Warehouses.AddRange(new Warehouse { Id = 30, BusinessId = 1, Name = "Warehouse 1", Address = "Dock" },
            new Warehouse { Id = 31, BusinessId = 2, Name = "Warehouse 2", Address = "Other dock" });
        db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "General" });
        db.Products.Add(new Product { Id = 1, BusinessId = 1, Name = "Product", ProductCode = "P-1", CategoryId = 1 });
        db.ProductVariants.Add(new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "Blue", Sku = "SKU-1", Attributes = "Color=Blue" });
        var date = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        db.PurchaseOrders.AddRange(new PurchaseOrder
        {
            Id = 20, PoNumber = "PO-TEST-20", SupplierId = 10, WarehouseId = 30, CreatedBy = 7,
            OrderDate = date, ExpectedDelivery = date.AddDays(1), Status = "approved",
            Notes = "PO notes", TotalAmount = 47.25m,
            Items = [new PurchaseOrderItem { Id = 22, VariantId = 1, Quantity = 5, Unit = "piece", UnitPrice = 10m, Discount = 10m, Tax = 5m }]
        }, new PurchaseOrder { Id = 21, PoNumber = "PO-OTHER", SupplierId = 11, WarehouseId = 31, OrderDate = date });
        db.Grns.AddRange(new Grn
        {
            Id = 40, PoId = 20, ReceivedDate = date, Notes = "Receipt notes", SupplierInvoiceNumber = "INV-40",
            SupplierInvoiceDate = date, Items = [new GrnItem { Id = 42, PoItemId = 22, ReceivedQty = 4, UnitCost = 8m, Notes = "Item notes" }]
        }, new Grn { Id = 41, PoId = 21, ReceivedDate = date });
    }
}
