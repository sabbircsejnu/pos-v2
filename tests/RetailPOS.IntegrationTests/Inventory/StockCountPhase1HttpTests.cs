using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
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

        // The export follows the company date format, so the test pins it instead of relying on the default.
        var settingsStore = _factory.Services.GetRequiredService<RetailPOS.Application.Settings.Abstractions.ISystemSettingsStore>();
        var company = (await settingsStore.GetAsync(CancellationToken.None)).Company;
        var originalDateFormat = company.DateFormat;
        company.DateFormat = "yyyy-MM-dd";
        await settingsStore.UpdateCompanyAsync(company, CancellationToken.None);
        HttpResponseMessage response;
        try
        {
            using var downloader = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Download");
            response = await downloader.GetAsync($"/api/stock-counts/{stockCountId}/download");
        }
        finally
        {
            company.DateFormat = originalDateFormat;
            await settingsStore.UpdateCompanyAsync(company, CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        var payload = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(payload);
        Assert.True(payload.Length > 100);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        Assert.NotNull(fileName);
        Assert.StartsWith("StockCount_SC-", fileName, StringComparison.Ordinal);
        Assert.EndsWith(".xlsx", fileName, StringComparison.OrdinalIgnoreCase);

        using var workbookStream = new MemoryStream(payload);
        using var workbook = new XLWorkbook(workbookStream);
        var worksheet = Assert.Single(workbook.Worksheets);
        Assert.Equal("Stock Count", worksheet.Name);
        Assert.Equal("Stock Count No", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Location", worksheet.Cell(2, 1).GetString());
        Assert.Equal("Stock Count Date", worksheet.Cell(3, 1).GetString());
        Assert.Equal("Generated By", worksheet.Cell(4, 1).GetString());
        Assert.Equal("Generated Time", worksheet.Cell(5, 1).GetString());
        Assert.Equal("yyyy-mm-dd", worksheet.Cell(3, 2).Style.DateFormat.Format);
        Assert.Equal("yyyy-mm-dd hh:mm", worksheet.Cell(5, 2).Style.DateFormat.Format);
        Assert.Equal("SL", worksheet.Cell(7, 1).GetString());
        Assert.Equal("Product Name", worksheet.Cell(7, 2).GetString());
        Assert.Equal("Product Code", worksheet.Cell(7, 3).GetString());
        Assert.Equal("Variant", worksheet.Cell(7, 4).GetString());
        Assert.Equal("Current Stock", worksheet.Cell(7, 5).GetString());
        Assert.Equal("Physical Count", worksheet.Cell(7, 6).GetString());
        Assert.Equal("Difference", worksheet.Cell(7, 7).GetString());
        Assert.Equal("Remarks", worksheet.Cell(7, 8).GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var expectedLines = await db.StockCountLines
            .AsNoTracking()
            .Where(line => line.StockCountId == stockCountId)
            .OrderBy(line => line.ProductName)
            .ThenBy(line => line.VariantName)
            .ToListAsync();
        Assert.Equal(expectedLines.Count + 7, worksheet.LastRowUsed()!.RowNumber());
        for (var index = 0; index < expectedLines.Count; index++)
        {
            var row = index + 8;
            var expected = expectedLines[index];
            Assert.Equal(index + 1, worksheet.Cell(row, 1).GetValue<int>());
            Assert.Equal(expected.ProductName, worksheet.Cell(row, 2).GetString());
            Assert.Equal(expected.ProductCode, worksheet.Cell(row, 3).GetString());
            Assert.Equal(expected.VariantName, worksheet.Cell(row, 4).GetString());
            Assert.Equal(expected.CurrentStock, worksheet.Cell(row, 5).GetValue<decimal>());
            Assert.Equal(expected.PhysicalStock, worksheet.Cell(row, 6).GetValue<decimal?>());
            Assert.Equal(expected.Difference, worksheet.Cell(row, 7).GetValue<decimal?>());
            Assert.Equal(expected.Remarks ?? string.Empty, worksheet.Cell(row, 8).GetString());
        }
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
        Assert.StartsWith("SC-", payload.StockCountNo, StringComparison.Ordinal);
        Assert.Equal("outlet", payload.LocationType);
        Assert.False(string.IsNullOrWhiteSpace(payload.LocationName));
        Assert.False(string.IsNullOrWhiteSpace(payload.GeneratedBy));
        Assert.False(payload.HasPostGenerationMovements);
        Assert.Equal(0, payload.PostGenerationMovementCount);
        Assert.Null(payload.LastPostGenerationMovementAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var expectedHeader = await db.StockCounts
            .AsNoTracking()
            .SingleAsync(stockCount => stockCount.Id == stockCountId);
        var expectedCompany = await db.Businesses
            .AsNoTracking()
            .Where(business => business.Id == expectedHeader.BusinessId)
            .Select(business => business.Name)
            .SingleAsync();
        var expectedLocation = await db.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.Id == expectedHeader.LocationId)
            .Select(outlet => outlet.Name)
            .SingleAsync();
        var expectedCreator = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == expectedHeader.CreatedBy)
            .Select(user => user.Name)
            .SingleAsync();
        Assert.Equal(expectedHeader.StockCountNo, payload.StockCountNo);
        Assert.Equal(expectedCompany, payload.CompanyName);
        Assert.Equal(expectedLocation, payload.LocationName);
        Assert.Equal(expectedHeader.StockCountDate, payload.StockCountDate);
        Assert.Equal(expectedCreator, payload.GeneratedBy);
        Assert.Equal(expectedHeader.CreatedAt, payload.GeneratedAt);
        var expectedLines = await db.StockCountLines
            .AsNoTracking()
            .Where(line => line.StockCountId == stockCountId)
            .OrderBy(line => line.ProductName)
            .ThenBy(line => line.VariantName)
            .Select(line => new { line.ProductName, line.VariantName })
            .ToListAsync();
        Assert.Equal(expectedLines.Select(line => line.ProductName), payload.Lines.Select(line => line.ProductName));
        Assert.Equal(expectedLines.Select(line => line.VariantName), payload.Lines.Select(line => line.VariantName));
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

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ForUnauthorizedLocation_ReturnsUnauthorized()
    {
        await ResetAndSeedAsync();
        using var globalCreator = CreateClient(userId: 203, permissions: "StockCount.ViewAll,StockCount.Create");
        var stockCountId = await CreateStockCountAsync(globalCreator, 12, "outlet");

        using var limitedViewer = CreateClient(userId: 201, permissions: "StockCount.ViewOwn");
        var response = await limitedViewer.GetAsync($"/api/stock-counts/{stockCountId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
    public async Task Upload_WithInvalidSerialNumber_ReturnsBadRequestWithoutChangingLines()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        using var workbookStream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Stock Count");
            worksheet.Cell(8, 1).Value = 2;
            worksheet.Cell(8, 6).Value = 12.5m;
            workbook.SaveAs(workbookStream);
        }

        using var actor = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Upload");
        using var form = new MultipartFormDataContent();
        form.Add(
            new ByteArrayContent(workbookStream.ToArray()),
            "file",
            "stock-count-upload.xlsx");
        var response = await actor.PostAsync($"/api/stock-counts/{stockCountId}/upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await ReadApiEnvelope(response);
        Assert.Contains("Invalid serial number at row 8", envelope.Message, StringComparison.Ordinal);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var line = await db.StockCountLines.SingleAsync(item => item.StockCountId == stockCountId);
        Assert.Null(line.PhysicalStock);
        Assert.Null(line.Difference);
        Assert.Null(line.Remarks);
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

            using var uploader = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Upload,StockCount.Submit,StockCount.Reject,StockCount.Reopen");
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
            long submittedBy;
            DateTime? submittedAt;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                var stockCount = await db.StockCounts.SingleAsync(item => item.Id == stockCountId);
                Assert.Equal(201, stockCount.SubmittedBy);
                Assert.NotNull(stockCount.SubmittedAt);
                submittedBy = stockCount.SubmittedBy!.Value;
                submittedAt = stockCount.SubmittedAt;
            }

            var rejectResponse = await uploader.PostAsJsonAsync(
                $"/api/stock-counts/{stockCountId}/reject",
                new { reason = "  Need recount  " });
            Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
            var rejected = await ReadApiData<StockCountResponse>(rejectResponse);
            Assert.Equal("Rejected", rejected!.Status);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                var stockCount = await db.StockCounts.SingleAsync(item => item.Id == stockCountId);
                Assert.Equal(201, stockCount.RejectedBy);
                Assert.NotNull(stockCount.RejectedAt);
                Assert.Equal("Need recount", stockCount.RejectionReason);
                Assert.Equal(submittedBy, stockCount.SubmittedBy);
                Assert.Equal(submittedAt, stockCount.SubmittedAt);
            }

            var reopenResponse = await uploader.PostAsync($"/api/stock-counts/{stockCountId}/reopen", null);
            Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);
            var reopened = await ReadApiData<StockCountResponse>(reopenResponse);
            Assert.Equal("Draft", reopened!.Status);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                var stockCount = await db.StockCounts.SingleAsync(item => item.Id == stockCountId);
                Assert.Null(stockCount.RejectedBy);
                Assert.Null(stockCount.RejectedAt);
                Assert.Null(stockCount.RejectionReason);
                Assert.Equal(submittedBy, stockCount.SubmittedBy);
                Assert.Equal(submittedAt, stockCount.SubmittedAt);
            }
        }
    }

    [Fact]
    public async Task Submit_WithoutUploadedCounts_ReturnsBadRequest()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        using var submitter = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Submit");
        var response = await submitter.PostAsync($"/api/stock-counts/{stockCountId}/submit", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await ReadApiEnvelope(response);
        Assert.Equal("Upload completed Excel before submitting stock count.", envelope.Message);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var stockCount = await db.StockCounts.SingleAsync(item => item.Id == stockCountId);
        Assert.Equal(StockCount.StatusDraft, stockCount.Status);
        Assert.Null(stockCount.SubmittedBy);
        Assert.Null(stockCount.SubmittedAt);
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
            var persistedStockCount = db.StockCounts.Single(item => item.Id == stockCountId);
            Assert.Equal(StockCount.StatusAdjustmentGenerated, persistedStockCount.Status);
            Assert.Equal(201, persistedStockCount.ApprovedBy);
            Assert.NotNull(persistedStockCount.ApprovedAt);
            Assert.Equal(11, adjustment.LocationId);
            Assert.Equal("outlet", adjustment.LocationType);
            Assert.Equal(StockAdjustment.StatusDraft, adjustment.Status);
            Assert.Equal(stockCountId, adjustment.SourceStockCountId);
            Assert.Equal(201, adjustment.AdjustedBy);
            Assert.StartsWith($"ADJ-{DateTime.UtcNow:yyyyMMdd}-", adjustment.AdjustmentNumber);
            Assert.NotEqual(default, adjustment.AdjustmentDate);
            Assert.NotEqual(default, adjustment.CreatedAt);
            Assert.NotEqual(default, adjustment.UpdatedAt);

            var line = db.StockAdjustmentLines.Single(l => l.StockAdjustmentId == adjustment.Id);
            Assert.Equal(10, line.PreviousQuantity);
            Assert.Equal(3, line.QuantityChange);
            Assert.Equal(13, line.NewQuantity);
            Assert.Equal("StockCountCorrection", line.Reason);
            Assert.Contains(generated.StockCountNo, line.Notes ?? string.Empty);
            Assert.NotEqual(default, line.CreatedAt);
            Assert.NotEqual(default, line.UpdatedAt);
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
    public async Task GenerateAdjustmentDraft_ConcurrentRequests_CreateSingleDraft()
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
            workbook.Worksheets.First().Cell(8, 6).Value = 13m;
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

            var request1 = actor.PostAsync($"/api/stock-counts/{stockCountId}/generate-adjustment-draft", null);
            var request2 = actor.PostAsync($"/api/stock-counts/{stockCountId}/generate-adjustment-draft", null);
            await Task.WhenAll(request1, request2);

            var response1 = await request1;
            var response2 = await request2;
            var statuses = new[] { response1.StatusCode, response2.StatusCode };
            Assert.Contains(HttpStatusCode.OK, statuses);
            Assert.Contains(HttpStatusCode.BadRequest, statuses);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Single(db.StockAdjustments);

            var adjustment = db.StockAdjustments.Single();
            Assert.Equal(stockCountId, adjustment.SourceStockCountId);
        }
    }

    [Fact]
    public async Task GeneratedAdjustment_Approval_MarksStockCountCompleted()
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
            workbook.Worksheets.First().Cell(8, 6).Value = 13m;
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
            Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);

            long adjustmentId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                adjustmentId = db.StockAdjustments.Single().Id;
            }

            using var approver = CreateClient(
                userId: 201,
                permissions: "stock_adjustments.view,stock_adjustments.create,stock_adjustments.approve,StockCount.ViewOwn");

            var submitAdjustment = await approver.PostAsync($"/api/stock-adjustments/{adjustmentId}/submit", null);
            Assert.Equal(HttpStatusCode.OK, submitAdjustment.StatusCode);

            var approveAdjustment = await approver.PostAsync($"/api/stock-adjustments/{adjustmentId}/approve", null);
            Assert.Equal(HttpStatusCode.OK, approveAdjustment.StatusCode);
        }

        var view = await creator.GetAsync($"/api/stock-counts/{stockCountId}");
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        var completed = await ReadApiData<StockCountResponse>(view);
        Assert.Equal("Completed", completed!.Status);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var approvedAdjustment = verifyDb.StockAdjustments.Single(x => x.SourceStockCountId == stockCountId);
        var auditEvent = await verifyDb.AuditEvents.SingleAsync(x => x.PrimaryEntityType == "StockAdjustment" && x.PrimaryEntityId == approvedAdjustment.Id.ToString() && x.ActionType == AuditActionType.Approve);
        Assert.Contains($"\"sourceStockCountId\":{stockCountId}", auditEvent.Metadata);
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
    public async Task Reject_WithOnlyStockCountRejectPermission_Succeeds()
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
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var rejected = await ReadApiData<StockCountResponse>(rejectResponse);
        Assert.Equal("Rejected", rejected!.Status);
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

    [Theory]
    [InlineData("approve")]
    [InlineData("generate-adjustment-draft")]
    public async Task Phase3Actions_WhenFeatureDisabled_ReturnForbidden(string actionRoute)
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient(userId: 201, permissions: "StockCount.ViewOwn,StockCount.Create");
        var stockCountId = await CreateStockCountAsync(creator, 11, "outlet");

        using var phase3DisabledFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FeatureFlags:StockCountPhase3Enabled"] = "false"
                });
            });
        });

        using var actor = phase3DisabledFactory.CreateClient();
        actor.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        actor.DefaultRequestHeaders.Add("X-Test-UserId", "201");
        actor.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        actor.DefaultRequestHeaders.Add("X-Test-Permissions", "StockCount.ViewOwn,StockCount.Approve");

        var response = await actor.PostAsync($"/api/stock-counts/{stockCountId}/{actionRoute}", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var envelope = await ReadApiEnvelope(response);
        Assert.Contains("Phase 3", envelope.Message, StringComparison.OrdinalIgnoreCase);
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

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    [InlineData(2, "outlet")]
    public async Task AssignedOutletSearch_FiltersBeforePaginationAndDoesNotLeakOtherLocations(
        int assignmentCount, string? locationType)
    {
        await ResetAndSeedAsync();
        await ConfigureAssignedAccessAsync(assignmentCount);
        using var client = CreateClient(201, "StockCount.ViewAll", "OutletManager");

        var response = await client.PostAsJsonAsync("/api/stock-counts/search",
            new { locationType, pageNumber = 1, pageSize = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(assignmentCount, json.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
        var data = await ReadApiData<StockCountListResponse>(response);
        Assert.Equal(Math.Min(assignmentCount, 1), data!.StockCounts.Count);
        Assert.All(data.StockCounts, item => Assert.Contains(item.LocationId, new long[] { 11, 12 }.Take(assignmentCount)));

        if (assignmentCount == 2)
        {
            var all = await client.PostAsJsonAsync("/api/stock-counts/search",
                new { locationType, pageSize = 50 });
            var allData = await ReadApiData<StockCountListResponse>(all);
            Assert.Equal(new long[] { 11, 12 }, allData!.StockCounts.Select(item => item.LocationId).OrderBy(id => id));
        }
    }

    [Theory]
    [InlineData(13, "outlet")]
    [InlineData(14, "outlet")]
    [InlineData(21, "warehouse")]
    public async Task AssignedOutletSearchAndCreate_RejectUnauthorizedLocation(long locationId, string locationType)
    {
        await ResetAndSeedAsync();
        await ConfigureAssignedAccessAsync(2);
        using var client = CreateClient(201, "StockCount.ViewAll,StockCount.Create", "OutletManager");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/stock-counts/search",
            new { locationId, locationType })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/stock-counts",
            new { locationId, locationType, stockCountDate = DateTime.UtcNow.Date })).StatusCode);
    }

    [Fact]
    public async Task AssignedOutletCreateAndDetails_AllowEveryAssignedOutlet()
    {
        await ResetAndSeedAsync();
        await ConfigureAssignedAccessAsync(2);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            foreach (var count in db.StockCounts)
                count.Status = StockCount.StatusCompleted;
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(201, "StockCount.ViewOwn,StockCount.Create", "OutletManager");
        foreach (var locationId in new long[] { 11, 12 })
        {
            var id = await CreateStockCountAsync(client, locationId, "outlet");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/stock-counts/{id}")).StatusCode);
        }
    }

    [Fact]
    public async Task AssignedOutletCountingAndApproval_AllowSecondaryAssignedOutlet()
    {
        await ResetAndSeedAsync();
        await ConfigureAssignedAccessAsync(2);
        using var client = CreateClient(201,
            "StockCount.ViewOwn,StockCount.Download,StockCount.Print,StockCount.Upload,StockCount.Submit,StockCount.Approve",
            "OutletManager");
        var download = await client.GetAsync("/api/stock-counts/9312/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/stock-counts/9312/print")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockCountLines.Add(new StockCountLine
            {
                StockCountId = 9312, ProductId = 301, VariantId = 401,
                ProductName = "StockCount Product", ProductCode = "SCP-001",
                VariantName = "Default Variant", CurrentStock = 20
            });
            await db.SaveChangesAsync();
        }
        download = await client.GetAsync("/api/stock-counts/9312/download");
        using var template = new MemoryStream(await download.Content.ReadAsByteArrayAsync());
        using var workbook = new XLWorkbook(template);
        workbook.Worksheets.First().Cell(8, 6).Value = 21m;
        using var upload = new MemoryStream();
        workbook.SaveAs(upload);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(upload.ToArray()), "file", "count.xlsx");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/stock-counts/9312/upload", form)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/stock-counts/9312/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/stock-counts/9312/approve", null)).StatusCode);
    }

    [Fact]
    public async Task AssignedOutletCreate_WithNoAssignments_RejectsStaleDefault()
    {
        await ResetAndSeedAsync();
        await ConfigureAssignedAccessAsync(0);
        using var client = CreateClient(201, "StockCount.Create", "OutletManager");
        var response = await client.PostAsJsonAsync("/api/stock-counts",
            new { stockCountDate = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("download")]
    [InlineData("print")]
    [InlineData("upload")]
    [InlineData("submit")]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("reopen")]
    [InlineData("generate-adjustment-draft")]
    public async Task AssignedOutletActions_RejectDirectAccessBeforeMutation(string action)
    {
        await ResetAndSeedAsync();
        await ConfigureAssignedAccessAsync(1);
        using var client = CreateClient(201,
            "StockCount.ViewAll,StockCount.Download,StockCount.Print,StockCount.Upload,StockCount.Submit,StockCount.Approve,StockCount.Reject,StockCount.Reopen",
            "OutletManager");
        var url = $"/api/stock-counts/9312/{action}".TrimEnd('/');
        HttpResponseMessage response;
        if (action is "" or "download" or "print")
            response = await client.GetAsync(url);
        else if (action == "upload")
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent([1]), "file", "count.xlsx");
            response = await client.PostAsync(url, form);
        }
        else
            response = await client.PostAsJsonAsync(url, new { reason = "Not authorized" });

        using (response)
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(StockCount.StatusDraft, (await db.StockCounts.FindAsync(9312L))!.Status);
        Assert.Empty(db.StockAdjustments);
    }

    [Theory]
    [InlineData("BusinessOwner")]
    [InlineData("Super Admin")]
    public async Task AssignedOutletSearch_PreservesPrivilegedBusinessScope(string role)
    {
        await ResetAndSeedAsync();
        await ConfigureAssignedAccessAsync(2);
        using var client = CreateClient(203, "StockCount.ViewAll", role);
        var response = await client.PostAsJsonAsync("/api/stock-counts/search", new { pageSize = 50 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadApiData<StockCountListResponse>(response);
        Assert.Equal(new long[] { 11, 12, 13, 21 }, data!.StockCounts.Select(item => item.LocationId).OrderBy(id => id));
    }

    private async Task ConfigureAssignedAccessAsync(int assignmentCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var user = await db.Users.FindAsync(201L);
        user!.InventoryLocationAccessScope = User.InventoryAccessSpecific;
        db.UserOutletAssignments.RemoveRange(db.UserOutletAssignments.Where(a => a.UserId == 201));
        await db.SaveChangesAsync();
        db.Businesses.Add(new Business { Id = 2, Name = "Other Business" });
        db.Outlets.AddRange(
            new Outlet { Id = 13, BusinessId = 1, Name = "Unassigned outlet" },
            new Outlet { Id = 14, BusinessId = 2, Name = "Foreign outlet" });
        foreach (var outletId in new long[] { 11, 12 }.Take(assignmentCount))
        {
            db.UserOutletAssignments.Add(new UserOutletAssignment
            {
                UserId = 201, BusinessId = 1, OutletId = outletId, IsPrimary = outletId == 11
            });
        }
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { UserId = 201, BusinessId = 1, OutletId = 13, IsActive = false },
            new UserOutletAssignment { UserId = 201, BusinessId = 2, OutletId = 14 });
        foreach (var locationId in new long[] { 11, 12, 13, 14, 21 })
            db.StockCounts.Add(new StockCount
            {
                Id = 9300 + locationId,
                StockCountNo = $"SC-ACCESS-{locationId}",
                BusinessId = locationId == 14 ? 2 : 1,
                LocationId = locationId,
                LocationType = locationId == 21 ? "warehouse" : "outlet",
                StockCountDate = DateTime.UtcNow.Date,
                Status = StockCount.StatusDraft,
                CreatedBy = 201,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Test Business" });

        db.Categories.Add(new Category { Id = 101, BusinessId = 1, Name = "Category", Description = "test" });

        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Outlet A", Address = "A" },
            new Outlet { Id = 12, BusinessId = 1, Name = "Outlet B", Address = "B" });

        db.Warehouses.Add(new Warehouse { Id = 21, BusinessId = 1, Name = "Warehouse A", Address = "W" });

        db.Products.Add(new Product
        {
            Id = 301,
            BusinessId = 1,
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
            BusinessId = 1,
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

        // StockCountOperation uses Step 5 assignments only, so each ordinary user is assigned its own outlet.
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { UserId = 201, BusinessId = 1, OutletId = 11, IsPrimary = true },
            new UserOutletAssignment { UserId = 202, BusinessId = 1, OutletId = 12, IsPrimary = true },
            new UserOutletAssignment { UserId = 203, BusinessId = 1, OutletId = 11, IsPrimary = true },
            new UserOutletAssignment { UserId = 203, BusinessId = 1, OutletId = 12 });
        db.UserWarehouseAssignments.Add(
            new UserWarehouseAssignment { UserId = 203, BusinessId = 1, WarehouseId = 21, IsPrimary = true });

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
        public string StockCountNo { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public string LocationType { get; set; } = string.Empty;
        public DateTime StockCountDate { get; set; }
        public string GeneratedBy { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public bool HasPostGenerationMovements { get; set; }
        public int PostGenerationMovementCount { get; set; }
        public DateTime? LastPostGenerationMovementAt { get; set; }
        public List<StockCountLineResponse> Lines { get; set; } = new();
    }
}
