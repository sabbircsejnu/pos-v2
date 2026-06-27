using System.Net;
using System.Net.Http.Json;
using RetailPOS.API.DTOs.Barcode;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Barcodes;

public class BarcodePdfHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public BarcodePdfHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GeneratePdf_WithSingleItemAndPrintPermission_ReturnsPdf()
    {
        using var client = CreateClient("barcode.view,barcode.print");

        var response = await client.PostAsJsonAsync("/api/barcodes/pdf", BuildPayload(singleItem: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith("barcode-labels-", ResolveFileName(response), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", ResolveFileName(response), StringComparison.OrdinalIgnoreCase);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
    }

    [Fact]
    public async Task GeneratePdf_WithoutPrintPermission_ReturnsForbidden()
    {
        using var client = CreateClient("barcode.view");

        var response = await client.PostAsJsonAsync("/api/barcodes/pdf", BuildPayload(singleItem: true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GeneratePdf_BulkWithoutBulkPermission_ReturnsForbidden()
    {
        using var client = CreateClient("barcode.view,barcode.print");

        var response = await client.PostAsJsonAsync("/api/barcodes/pdf", BuildPayload(singleItem: false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GeneratePdf_BulkWithBulkPermission_ReturnsPdf()
    {
        using var client = CreateClient("barcode.view,barcode.print,barcode.bulk_print");

        var response = await client.PostAsJsonAsync("/api/barcodes/pdf", BuildPayload(singleItem: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("barcode-labels-", ResolveFileName(response), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", ResolveFileName(response), StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFileName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        return disposition?.FileNameStar
            ?? disposition?.FileName?.Trim('"')
            ?? string.Empty;
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7001");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static RecordBarcodePrintHistoryDto BuildPayload(bool singleItem)
    {
        var dto = new RecordBarcodePrintHistoryDto
        {
            TemplateId = null,
            OutletId = null,
            LabelWidthMm = 60,
            LabelHeightMm = 40,
            PrintMode = "pdf",
            SourceModule = "test",
            SourceReferenceType = "test",
            SourceReferenceId = 1,
            Items =
            [
                new CreateBarcodePrintHistoryItemDto
                {
                    VariantId = 1001,
                    ProductName = "Classic T-Shirt",
                    VariantName = "Red / Small",
                    VariantSku = "TSH-RED-S",
                    BarcodeValue = "BRC-RED-S",
                    VariantAttributes = "Color: Red | Size: S",
                    SellingPrice = 7490,
                    QuantityPrinted = 2
                }
            ]
        };

        if (!singleItem)
        {
            dto.Items.Add(new CreateBarcodePrintHistoryItemDto
            {
                VariantId = 1002,
                ProductName = "Classic T-Shirt",
                VariantName = "Black / Medium",
                VariantSku = "TSH-BLK-M",
                BarcodeValue = "BRC-BLK-M",
                VariantAttributes = "Color: Black | Size: M",
                SellingPrice = 7590,
                QuantityPrinted = 1
            });
        }

        return dto;
    }
}
