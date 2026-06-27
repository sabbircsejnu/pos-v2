using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public class StockCountPhase1HttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public StockCountPhase1HttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_GeneratesSnapshotAndDraft()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create,StockCount.Download,StockCount.Print");

        var response = await client.PostAsJsonAsync("/api/stock-counts", new
        {
            stockCountDate = DateTime.UtcNow.Date,
            locationId = 11L,
            locationType = "outlet",
            remarks = "Monthly count"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadApiData<StockCountResponse>(response);
        Assert.NotNull(created);
        Assert.Equal("Draft", created!.Status);
        Assert.Equal(11, created.LocationId);
        Assert.Equal("outlet", created.LocationType);
        Assert.True(created.TotalItems >= 1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.StockCounts.FindAsync(created.Id);
        Assert.NotNull(persisted);
        Assert.Equal(1, db.StockCountLines.Count(l => l.StockCountId == created.Id));
    }

    [Fact]
    public async Task Create_WhenActiveCountExistsForLocation_ReturnsBadRequest()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");

        var first = await client.PostAsJsonAsync("/api/stock-counts", new
        {
            stockCountDate = DateTime.UtcNow.Date,
            locationId = 11L,
            locationType = "outlet"
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/stock-counts", new
        {
            stockCountDate = DateTime.UtcNow.Date,
            locationId = 11L,
            locationType = "outlet"
        });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var envelope = await ReadApiEnvelope(second);
        Assert.Contains("active stock count", envelope.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithoutCreatePermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(userId: 201, permissions: "StockCount.ViewOwn");

        var response = await client.PostAsJsonAsync("/api/stock-counts", new
        {
            stockCountDate = DateTime.UtcNow.Date,
            locationId = 11L,
            locationType = "outlet"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithViewOwn_IsScopedToDefaultLocation()
    {
        await ResetAndSeedAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockCounts.AddRange(
                new StockCount
                {
                    Id = 9001,
                    StockCountNo = "SC-20260627-0001",
                    BusinessId = 1,
                    LocationId = 11,
                    LocationType = "outlet",
                    StockCountDate = DateTime.UtcNow.Date,
                    Status = StockCount.StatusDraft,
                    TotalItems = 1,
                    CreatedBy = 201,
                    CreatedAt = DateTime.UtcNow
                },
                new StockCount
                {
                    Id = 9002,
                    StockCountNo = "SC-20260627-0002",
                    BusinessId = 1,
                    LocationId = 12,
                    LocationType = "outlet",
                    StockCountDate = DateTime.UtcNow.Date,
                    Status = StockCount.StatusDraft,
                    TotalItems = 1,
                    CreatedBy = 202,
                    CreatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(userId: 201, permissions: "StockCount.ViewOwn");
        var response = await client.PostAsJsonAsync("/api/stock-counts/search", new { pageNumber = 1, pageSize = 50 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadApiData<StockCountListResponse>(response);
        Assert.NotNull(data);
        Assert.NotEmpty(data!.StockCounts);
        Assert.All(data.StockCounts, x => Assert.Equal(11, x.LocationId));
    }

    [Fact]
    public async Task Search_WithViewAll_CanSeeMultipleLocations()
    {
        await ResetAndSeedAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockCounts.AddRange(
                new StockCount
                {
                    Id = 9101,
                    StockCountNo = "SC-20260627-0101",
                    BusinessId = 1,
                    LocationId = 11,
                    LocationType = "outlet",
                    StockCountDate = DateTime.UtcNow.Date,
                    Status = StockCount.StatusDraft,
                    TotalItems = 1,
                    CreatedBy = 201,
                    CreatedAt = DateTime.UtcNow
                },
                new StockCount
                {
                    Id = 9102,
                    StockCountNo = "SC-20260627-0102",
                    BusinessId = 1,
                    LocationId = 12,
                    LocationType = "outlet",
                    StockCountDate = DateTime.UtcNow.Date,
                    Status = StockCount.StatusDraft,
                    TotalItems = 1,
                    CreatedBy = 202,
                    CreatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(userId: 203, permissions: "StockCount.ViewAll");
        var response = await client.PostAsJsonAsync("/api/stock-counts/search", new { pageNumber = 1, pageSize = 50 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadApiData<StockCountListResponse>(response);
        Assert.NotNull(data);
        Assert.Contains(data!.StockCounts, x => x.LocationId == 11);
        Assert.Contains(data.StockCounts, x => x.LocationId == 12);
    }

    [Fact]
    public async Task Download_WithPermission_ReturnsExcelFile()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");

        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        using var downloader = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Download");
        var response = await downloader.GetAsync($"/api/stock-counts/{stockCountId}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        var payload = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(payload);
        Assert.True(payload.Length > 100);
    }

    [Fact]
    public async Task Download_WithoutPermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");

        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        using var noDownload = CreateClient(userId: 201, permissions: "StockCount.ViewOwn");
        var response = await noDownload.GetAsync($"/api/stock-counts/{stockCountId}/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Print_WithoutPermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");

        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        using var noPrint = CreateClient(userId: 201, permissions: "StockCount.ViewOwn");
        var response = await noPrint.GetAsync($"/api/stock-counts/{stockCountId}/print");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Print_WithPermission_ReturnsPrintablePayload()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");

        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        using var printer = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Print");
        var response = await printer.GetAsync($"/api/stock-counts/{stockCountId}/print");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadApiData<StockCountPrintResponse>(response);
        Assert.NotNull(payload);
        Assert.Equal(stockCountId, payload!.Id);
        Assert.False(string.IsNullOrWhiteSpace(payload.CompanyName));
        Assert.NotEmpty(payload.Lines);
    }

    [Fact]
    public async Task Create_ForUnauthorizedLocation_ReturnsUnauthorized()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");

        var response = await client.PostAsJsonAsync("/api/stock-counts", new
        {
            stockCountDate = DateTime.UtcNow.Date,
            locationId = 12L,
            locationType = "outlet"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ForUnauthorizedLocation_ReturnsUnauthorized()
    {
        await ResetAndSeedAsync();
        using var globalCreator = CreateClient(userId: 203, permissions: "StockCount.ViewAll,StockCount.Create");
        var stockCountId = await CreateStockCountAsync(globalCreator, 12, "outlet");

        using var limitedViewer = CreateClient(userId: 201, permissions: "StockCount.ViewOwn");
        var response = await limitedViewer.GetAsync($"/api/stock-counts/{stockCountId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithPermission_UpdatesPhysicalStockDifferenceAndRemarks()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create,StockCount.Download");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        var templateResponse = await creator.GetAsync($"/api/stock-counts/{stockCountId}/download");
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        var templateBytes = await templateResponse.Content.ReadAsByteArrayAsync();

        using var templateStream = new MemoryStream(templateBytes);
        using var workbook = new XLWorkbook(templateStream);
        var ws = workbook.Worksheets.First();

        ws.Cell(8, 6).Value = 12.5m;
        ws.Cell(8, 8).Value = "Count completed";

        await using var uploadStream = new MemoryStream();
        workbook.SaveAs(uploadStream);
        uploadStream.Position = 0;

        using var actor = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Upload");
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(uploadStream.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", "stock-count-upload.xlsx");

        var uploadResponse = await actor.PostAsync($"/api/stock-counts/{stockCountId}/upload", form);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        var uploaded = await ReadApiData<StockCountResponse>(uploadResponse);
        Assert.NotNull(uploaded);
        var line = Assert.Single(uploaded!.Lines);
        Assert.Equal(12.5m, line.PhysicalStock);
        Assert.Equal(2.5m, line.Difference);
        Assert.Equal("Count completed", line.Remarks);
    }

    [Fact]
    public async Task SubmitRejectReopen_WithPermission_TransitionsStatus()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create,StockCount.Download");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        var templateResponse = await creator.GetAsync($"/api/stock-counts/{stockCountId}/download");
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        var templateBytes = await templateResponse.Content.ReadAsByteArrayAsync();

        using (var templateStream = new MemoryStream(templateBytes))
        using (var workbook = new XLWorkbook(templateStream))
        await using (var uploadStream = new MemoryStream())
        {
            workbook.Worksheets.First().Cell(8, 6).Value = 11m;
            workbook.SaveAs(uploadStream);
            uploadStream.Position = 0;

            using var uploader = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Upload,StockCount.Submit,StockCount.Approve,StockCount.Reopen");
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(uploadStream.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(fileContent, "file", "stock-count-upload.xlsx");

            var uploadResponse = await uploader.PostAsync($"/api/stock-counts/{stockCountId}/upload", form);
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

            var submitResponse = await uploader.PostAsync($"/api/stock-counts/{stockCountId}/submit", null);
            Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
            var submitted = await ReadApiData<StockCountResponse>(submitResponse);
            Assert.Equal("Submitted", submitted!.Status);

            var rejectResponse = await uploader.PostAsJsonAsync($"/api/stock-counts/{stockCountId}/reject", new { reason = "Need recount" });
            Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
            var rejected = await ReadApiData<StockCountResponse>(rejectResponse);
            Assert.Equal("Rejected", rejected!.Status);

            var reopenResponse = await uploader.PostAsync($"/api/stock-counts/{stockCountId}/reopen", null);
            Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);
            var reopened = await ReadApiData<StockCountResponse>(reopenResponse);
            Assert.Equal("Draft", reopened!.Status);
        }
    }

    [Fact]
    public async Task Approve_WithMissingPhysicalCounts_ReturnsBadRequest()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create,StockCount.Download");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        var templateResponse = await creator.GetAsync($"/api/stock-counts/{stockCountId}/download");
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        var templateBytes = await templateResponse.Content.ReadAsByteArrayAsync();

        using (var templateStream = new MemoryStream(templateBytes))
        using (var workbook = new XLWorkbook(templateStream))
        await using (var uploadStream = new MemoryStream())
        {
            // Keep physical count empty; only remarks are uploaded.
            workbook.Worksheets.First().Cell(8, 8).Value = "Partial entry";
            workbook.SaveAs(uploadStream);
            uploadStream.Position = 0;

            using var actor = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Upload,StockCount.Submit,StockCount.Approve");
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(uploadStream.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(fileContent, "file", "stock-count-upload.xlsx");

            var uploadResponse = await actor.PostAsync($"/api/stock-counts/{stockCountId}/upload", form);
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

            var submitResponse = await actor.PostAsync($"/api/stock-counts/{stockCountId}/submit", null);
            Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

            var approveResponse = await actor.PostAsync($"/api/stock-counts/{stockCountId}/approve", null);
            Assert.Equal(HttpStatusCode.BadRequest, approveResponse.StatusCode);
            var envelope = await ReadApiEnvelope(approveResponse);
            Assert.Contains("physical counts", envelope.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Approve_AndGenerateAdjustmentDraft_TransitionsAndCreatesDraftLines()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create,StockCount.Download");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        var templateResponse = await creator.GetAsync($"/api/stock-counts/{stockCountId}/download");
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        var templateBytes = await templateResponse.Content.ReadAsByteArrayAsync();

        using (var templateStream = new MemoryStream(templateBytes))
        using (var workbook = new XLWorkbook(templateStream))
        await using (var uploadStream = new MemoryStream())
        {
            workbook.Worksheets.First().Cell(8, 6).Value = 13m; // difference +3 from seeded current 10
            workbook.SaveAs(uploadStream);
            uploadStream.Position = 0;

            using var actor = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Upload,StockCount.Submit,StockCount.Approve");
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(uploadStream.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(fileContent, "file", "stock-count-upload.xlsx");

            var uploadResponse = await actor.PostAsync($"/api/stock-counts/{stockCountId}/upload", form);
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

            var submitResponse = await actor.PostAsync($"/api/stock-counts/{stockCountId}/submit", null);
            Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

            var approveResponse = await actor.PostAsync($"/api/stock-counts/{stockCountId}/approve", null);
            Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
            var approved = await ReadApiData<StockCountResponse>(approveResponse);
            Assert.Equal("Approved", approved!.Status);

            var generateResponse = await actor.PostAsync($"/api/stock-counts/{stockCountId}/generate-adjustment-draft", null);
            Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
            var generated = await ReadApiData<StockCountResponse>(generateResponse);
            Assert.Equal("AdjustmentGenerated", generated!.Status);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var adjustment = db.StockAdjustments.Single();
            Assert.Equal(11, adjustment.LocationId);
            Assert.Equal("outlet", adjustment.LocationType);
            Assert.Equal(StockAdjustment.StatusDraft, adjustment.Status);

            var line = db.StockAdjustmentLines.Single(l => l.StockAdjustmentId == adjustment.Id);
            Assert.Equal(3, line.QuantityChange);
            Assert.Equal("StockCountCorrection", line.Reason);
            Assert.Contains(generated.StockCountNo, line.Notes ?? string.Empty);
        }
    }

    [Fact]
    public async Task GenerateAdjustmentDraft_WhenAlreadyGenerated_ReturnsBadRequest()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create,StockCount.Download");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        var templateResponse = await creator.GetAsync($"/api/stock-counts/{stockCountId}/download");
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        var templateBytes = await templateResponse.Content.ReadAsByteArrayAsync();

        using (var templateStream = new MemoryStream(templateBytes))
        using (var workbook = new XLWorkbook(templateStream))
        await using (var uploadStream = new MemoryStream())
        {
            workbook.Worksheets.First().Cell(8, 6).Value = 12m;
            workbook.SaveAs(uploadStream);
            uploadStream.Position = 0;

            using var actor = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Upload,StockCount.Submit,StockCount.Approve");
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(uploadStream.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(fileContent, "file", "stock-count-upload.xlsx");

            Assert.Equal(HttpStatusCode.OK, (await actor.PostAsync($"/api/stock-counts/{stockCountId}/upload", form)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await actor.PostAsync($"/api/stock-counts/{stockCountId}/submit", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await actor.PostAsync($"/api/stock-counts/{stockCountId}/approve", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await actor.PostAsync($"/api/stock-counts/{stockCountId}/generate-adjustment-draft", null)).StatusCode);

            var duplicateGenerate = await actor.PostAsync($"/api/stock-counts/{stockCountId}/generate-adjustment-draft", null);
            Assert.Equal(HttpStatusCode.BadRequest, duplicateGenerate.StatusCode);
            var envelope = await ReadApiEnvelope(duplicateGenerate);
            Assert.Contains("Approved", envelope.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GenerateAdjustmentDraft_WithZeroDifferences_ReturnsBadRequest()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create,StockCount.Download");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        var templateResponse = await creator.GetAsync($"/api/stock-counts/{stockCountId}/download");
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        var templateBytes = await templateResponse.Content.ReadAsByteArrayAsync();

        using (var templateStream = new MemoryStream(templateBytes))
        using (var workbook = new XLWorkbook(templateStream))
        await using (var uploadStream = new MemoryStream())
        {
            // Seeded current stock is 10, so this yields zero difference.
            workbook.Worksheets.First().Cell(8, 6).Value = 10m;
            workbook.SaveAs(uploadStream);
            uploadStream.Position = 0;

            using var actor = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Upload,StockCount.Submit,StockCount.Approve");
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(uploadStream.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(fileContent, "file", "stock-count-upload.xlsx");

            Assert.Equal(HttpStatusCode.OK, (await actor.PostAsync($"/api/stock-counts/{stockCountId}/upload", form)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await actor.PostAsync($"/api/stock-counts/{stockCountId}/submit", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await actor.PostAsync($"/api/stock-counts/{stockCountId}/approve", null)).StatusCode);

            var generateResponse = await actor.PostAsync($"/api/stock-counts/{stockCountId}/generate-adjustment-draft", null);
            Assert.Equal(HttpStatusCode.BadRequest, generateResponse.StatusCode);
            var envelope = await ReadApiEnvelope(generateResponse);
            Assert.Contains("No stock differences", envelope.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Reject_WithOnlyStockCountRejectPermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create,StockCount.Download");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        var templateResponse = await creator.GetAsync($"/api/stock-counts/{stockCountId}/download");
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        var templateBytes = await templateResponse.Content.ReadAsByteArrayAsync();

        using (var templateStream = new MemoryStream(templateBytes))
        using (var workbook = new XLWorkbook(templateStream))
        await using (var uploadStream = new MemoryStream())
        {
            workbook.Worksheets.First().Cell(8, 6).Value = 11m;
            workbook.SaveAs(uploadStream);
            uploadStream.Position = 0;

            using var submitter = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Upload,StockCount.Submit");
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(uploadStream.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(fileContent, "file", "stock-count-upload.xlsx");

            Assert.Equal(HttpStatusCode.OK, (await submitter.PostAsync($"/api/stock-counts/{stockCountId}/upload", form)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await submitter.PostAsync($"/api/stock-counts/{stockCountId}/submit", null)).StatusCode);
        }

        using var rejectOnly = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Reject");
        var rejectResponse = await rejectOnly.PostAsJsonAsync($"/api/stock-counts/{stockCountId}/reject", new { reason = "No permission now" });
        Assert.Equal(HttpStatusCode.Forbidden, rejectResponse.StatusCode);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("generate-adjustment-draft")]
    public async Task Phase3Actions_WithoutApprovePermission_ReturnForbidden(string actionRoute)
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        using var actor = CreateClient(userId: 201, permissions: "StockCount.ViewOwn");
        var response = actionRoute == "reject"
            ? await actor.PostAsJsonAsync($"/api/stock-counts/{stockCountId}/reject", new { reason = "x" })
            : await actor.PostAsync($"/api/stock-counts/{stockCountId}/{actionRoute}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Snapshot_IsImmutable_AndMovementWarningAppears()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");

        var createResponse = await client.PostAsJsonAsync("/api/stock-counts", new
        {
            stockCountDate = DateTime.UtcNow.Date,
            locationId = 11L,
            locationType = "outlet"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await ReadApiData<StockCountResponse>(createResponse);
        Assert.NotNull(created);

        string originalProductName;
        string originalProductCode;
        string originalVariantName;
        decimal originalCurrentStock;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var line = db.StockCountLines.Single(l => l.StockCountId == created!.Id);
            originalProductName = line.ProductName;
            originalProductCode = line.ProductCode;
            originalVariantName = line.VariantName;
            originalCurrentStock = line.CurrentStock;

            var product = db.Products.Single(p => p.Id == line.ProductId);
            product.Name = "Changed Product Name";
            product.ProductCode = "CHANGED-CODE";

            var variant = db.ProductVariants.Single(v => v.Id == line.VariantId);
            variant.Name = "Changed Variant Name";

            var inv = db.Inventories.Single(i => i.VariantId == line.VariantId && i.LocationId == 11 && i.LocationType == "outlet");
            inv.Quantity = inv.Quantity + 7;

            db.StockLedgers.Add(new StockLedger
            {
                VariantId = line.VariantId,
                LocationId = 11,
                LocationType = "outlet",
                TransactionType = StockLedgerTransactionType.Adjustment,
                QtyIn = 7,
                QtyOut = 0,
                BalanceAfter = inv.Quantity,
                ReferenceType = StockLedgerReferenceType.StockAdjustment,
                ReferenceId = 777,
                CreatedBy = 201,
                CreatedAt = DateTime.UtcNow.AddMinutes(1)
            });

            await db.SaveChangesAsync();
        }

        var viewResponse = await client.GetAsync($"/api/stock-counts/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, viewResponse.StatusCode);

        var viewed = await ReadApiData<StockCountResponse>(viewResponse);
        Assert.NotNull(viewed);
        Assert.NotEmpty(viewed!.Lines);

        var viewedLine = viewed.Lines[0];
        Assert.Equal(originalProductName, viewedLine.ProductName);
        Assert.Equal(originalProductCode, viewedLine.ProductCode);
        Assert.Equal(originalVariantName, viewedLine.VariantName);
        Assert.Equal(originalCurrentStock, viewedLine.CurrentStock);

        Assert.True(viewed.HasPostGenerationMovements);
        Assert.True(viewed.PostGenerationMovementCount > 0);
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Test Business" });

        db.Categories.Add(new Category { Id = 101, Name = "Category", Description = "test" });

        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Outlet A", Address = "A" },
            new Outlet { Id = 12, BusinessId = 1, Name = "Outlet B", Address = "B" });

        db.Warehouses.Add(new Warehouse { Id = 21, BusinessId = 1, Name = "Warehouse A", Address = "W" });

        db.Products.Add(new Product
        {
            Id = 301,
            Name = "StockCount Product",
            ProductCode = "SCP-001",
            CategoryId = 101,
            BasePrice = 10m,
            CostPrice = 5m,
            Status = ProductStatus.Active,
            HasVariants = true
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 401,
            ProductId = 301,
            Name = "Default Variant",
            Sku = "SCP-001-DEF",
            Barcode = "SCP-BC-001",
            Attributes = "{}"
        });

        db.Inventories.AddRange(
            new RetailPOS.Core.Entities.Inventory { Id = 501, VariantId = 401, LocationId = 11, LocationType = "outlet", Quantity = 10 },
            new RetailPOS.Core.Entities.Inventory { Id = 502, VariantId = 401, LocationId = 12, LocationType = "outlet", Quantity = 20 },
            new RetailPOS.Core.Entities.Inventory { Id = 503, VariantId = 401, LocationId = 21, LocationType = "warehouse", Quantity = 30 });

        db.Users.AddRange(
            new User
            {
                Id = 201,
                BusinessId = 1,
                Name = "Own Outlet User",
                Email = "own@test.local",
                PasswordHash = "x",
                OutletId = 11,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            },
            new User
            {
                Id = 202,
                BusinessId = 1,
                Name = "Other Outlet User",
                Email = "other@test.local",
                PasswordHash = "x",
                OutletId = 12,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            },
            new User
            {
                Id = 203,
                BusinessId = 1,
                Name = "Business Owner User",
                Email = "owner@test.local",
                PasswordHash = "x",
                InventoryLocationAccessScope = User.InventoryAccessAll,
                IsActive = true
            });

        await db.SaveChangesAsync();
    }

    private async Task<long> CreateStockCountAsync(HttpClient client, long locationId, string locationType)
    {
        var response = await client.PostAsJsonAsync("/api/stock-counts", new
        {
            stockCountDate = DateTime.UtcNow.Date,
            locationId,
            locationType
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadApiData<StockCountResponse>(response);
        Assert.NotNull(created);
        return created!.Id;
    }

    private HttpClient CreateClient(long userId, string permissions, string role = "AccountsAdmin", long businessId = 1)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static async Task<T?> ReadApiData<T>(HttpResponseMessage response) where T : class
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
        return envelope?.Data;
    }

    private static async Task<ApiEnvelope<object>> ReadApiEnvelope(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        return (await JsonSerializer.DeserializeAsync<ApiEnvelope<object>>(stream, JsonOptions))!;
    }

    private sealed class ApiEnvelope<T> where T : class
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class StockCountListResponse
    {
        public List<StockCountResponse> StockCounts { get; set; } = new();
    }

    private sealed class StockCountResponse
    {
        public long Id { get; set; }
        public string StockCountNo { get; set; } = string.Empty;
        public long LocationId { get; set; }
        public string LocationType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalItems { get; set; }
        public bool HasPostGenerationMovements { get; set; }
        public int PostGenerationMovementCount { get; set; }
        public List<StockCountLineResponse> Lines { get; set; } = new();
    }

    private sealed class StockCountLineResponse
    {
        public string ProductName { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string VariantName { get; set; } = string.Empty;
        public decimal CurrentStock { get; set; }
        public decimal? PhysicalStock { get; set; }
        public decimal? Difference { get; set; }
        public string? Remarks { get; set; }
    }

    private sealed class StockCountPrintResponse
    {
        public long Id { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public List<StockCountLineResponse> Lines { get; set; } = new();
    }
}
