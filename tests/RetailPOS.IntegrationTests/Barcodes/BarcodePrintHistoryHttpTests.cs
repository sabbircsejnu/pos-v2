using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Barcode;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Barcodes;

public sealed class BarcodePrintHistoryHttpTests : IClassFixture<RetailPosApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RetailPosApiFactory _factory;

    public BarcodePrintHistoryHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SearchPrintHistory_PreservesBusinessDateFiltersAndPaging()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1);

        var response = await client.PostAsJsonAsync(
            "/api/barcodes/print-history/search",
            new BarcodePrintHistorySearchDto
            {
                StartDate = new DateTime(2026, 1, 2),
                PageNumber = 1,
                PageSize = 1
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadApiData<BarcodePrintHistoryListDto>(response);
        Assert.NotNull(result);
        Assert.Equal(2, result!.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(103, Assert.Single(result.Rows).Id);
    }

    [Fact]
    public async Task GetPrintHistory_PreservesDetailItemsAndBusinessAuthorization()
    {
        await ResetAndSeedAsync();
        using var businessClient = CreateClient(1);

        var detailResponse = await businessClient.GetAsync("/api/barcodes/print-history/101");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadApiData<BarcodePrintHistoryDto>(detailResponse);
        Assert.NotNull(detail);
        Assert.Equal("detail-source", detail!.SourceModule);
        var item = Assert.Single(detail.Items);
        Assert.Equal(21, item.VariantId);
        Assert.Equal("SKU-21", item.VariantSku);

        using var otherBusinessClient = CreateClient(2);
        var forbiddenResponse = await otherBusinessClient.GetAsync("/api/barcodes/print-history/101");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        var missingResponse = await businessClient.GetAsync("/api/barcodes/print-history/999");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task RecordSinglePrint_PreservesBusinessAttributionNormalizationAndItems()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1, "barcode.view,barcode.print");
        var request = new RecordBarcodePrintHistoryDto
        {
            PrintMode = "PDF",
            LabelWidthMm = 58,
            LabelHeightMm = 32,
            SourceModule = " Products ",
            SourceReferenceType = " Variant ",
            SourceReferenceId = 21,
            Items =
            [
                new CreateBarcodePrintHistoryItemDto
                {
                    VariantId = 21,
                    ProductName = "Barcode Product",
                    VariantName = "Default",
                    VariantSku = "SKU-21",
                    BarcodeValue = "BARCODE-21",
                    VariantAttributes = "{\"Color\":\"Blue\"}",
                    SellingPrice = 12.50m,
                    QuantityPrinted = 3
                }
            ]
        };

        var response = await client.PostAsJsonAsync("/api/barcodes/print-history/single", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadApiData<BarcodePrintHistoryDto>(response);
        Assert.NotNull(result);
        Assert.Equal(3, result!.TotalLabels);
        Assert.Equal("pdf", result.PrintMode);
        Assert.Equal("Products", result.SourceModule);
        Assert.Equal("Variant", result.SourceReferenceType);
        var responseItem = Assert.Single(result.Items);
        Assert.Equal(3, responseItem.QuantityPrinted);
        Assert.Equal(12.50m, responseItem.SellingPrice);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var saved = await db.BarcodePrintHistories
            .Include(history => history.Items)
            .SingleAsync(history => history.Id == result.Id);
        Assert.Equal(1, saved.BusinessId);
        Assert.Equal(7001, saved.PrintedByUserId);
        Assert.Equal(3, saved.TotalLabels);
        Assert.Equal("pdf", saved.PrintMode);
        Assert.Equal("Products", saved.SourceModule);
        Assert.Equal("Variant", saved.SourceReferenceType);
        Assert.Equal(21, saved.SourceReferenceId);
        Assert.Equal(3, Assert.Single(saved.Items).QuantityPrinted);
    }

    [Fact]
    public async Task RecordBulkPrint_RequiresBulkPermission()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1, "barcode.view,barcode.print");

        var response = await client.PostAsJsonAsync(
            "/api/barcodes/print-history/bulk",
            new RecordBarcodePrintHistoryDto
            {
                Items =
                [
                    new CreateBarcodePrintHistoryItemDto
                    {
                        VariantId = 21,
                        ProductName = "Barcode Product",
                        VariantName = "Default",
                        VariantSku = "SKU-21",
                        QuantityPrinted = 1
                    }
                ]
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RecordSinglePrint_RejectsTemplateFromAnotherBusiness()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.BarcodeTemplates.Add(new BarcodeTemplate
            {
                Id = 401,
                BusinessId = 2,
                Name = "Other Business"
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(1, "barcode.view,barcode.print");
        var response = await client.PostAsJsonAsync(
            "/api/barcodes/print-history/single",
            new RecordBarcodePrintHistoryDto
            {
                TemplateId = 401,
                Items =
                [
                    new CreateBarcodePrintHistoryItemDto
                    {
                        VariantId = 21,
                        ProductName = "Barcode Product",
                        VariantName = "Default",
                        VariantSku = "SKU-21",
                        QuantityPrinted = 1
                    }
                ]
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(4, await verificationDb.BarcodePrintHistories.CountAsync());
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Barcode One" },
            new Business { Id = 2, Name = "Barcode Two" });
        db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "Barcode Products" });
        db.Products.Add(new Product
        {
            Id = 11,
            BusinessId = 1,
            Name = "Barcode Product",
            ProductCode = "BAR-001",
            CategoryId = 1,
            Status = ProductStatus.Active
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 21,
            BusinessId = 1,
            ProductId = 11,
            Name = "Default",
            Sku = "SKU-21"
        });
        db.BarcodePrintHistories.AddRange(
            History(101, 1, new DateTime(2026, 1, 1), "detail-source"),
            History(102, 1, new DateTime(2026, 1, 2), "search-source"),
            History(103, 1, new DateTime(2026, 1, 3), "search-source"),
            History(201, 2, new DateTime(2026, 1, 4), "other-business"));
        db.BarcodePrintHistoryItems.Add(new BarcodePrintHistoryItem
        {
            Id = 301,
            PrintHistoryId = 101,
            VariantId = 21,
            ProductName = "Barcode Product",
            VariantName = "Default",
            VariantSku = "SKU-21",
            BarcodeValue = "BARCODE-21",
            QuantityPrinted = 2
        });
        await db.SaveChangesAsync();
    }

    private HttpClient CreateClient(long businessId, string permissions = "barcode.view")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7001");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static BarcodePrintHistory History(
        long id,
        long businessId,
        DateTime printedAt,
        string sourceModule) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            PrintMode = "pdf",
            LabelWidthMm = 60,
            LabelHeightMm = 40,
            TotalLabels = 1,
            SourceModule = sourceModule,
            PrintedAt = printedAt
        };

    private static async Task<T?> ReadApiData<T>(HttpResponseMessage response)
        where T : class
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
        return envelope?.Data;
    }

    private sealed class ApiEnvelope<T>
        where T : class
    {
        public T? Data { get; set; }
    }
}
