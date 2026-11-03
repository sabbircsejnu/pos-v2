using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

public class PosCashSessionHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PosCashSessionHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ActiveSession_ReturnsNull_WhenNoOpenSessionExists()
    {
        await ResetAndSeedAsync(includeOpenSession: false);
        using var client = CreatePosClient();

        var response = await client.GetAsync("/api/pos/cash-sessions/active?outletId=5001&terminalId=8001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await ReadApiEnvelope<CashSessionDto?>(response);
        Assert.NotNull(envelope);
        Assert.Null(envelope!.Data);
    }

    [Fact]
    public async Task ActiveAndSummary_DenyUnauthorizedOutletBeforeReturningSessionData()
    {
        await ResetAndSeedAsync(includeOpenSession: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet
            {
                Id = 5002,
                BusinessId = 1,
                Name = "Restricted outlet",
                Address = "Dhaka"
            });
            db.PosTerminals.Add(new PosTerminal
            {
                Id = 8002,
                OutletId = 5002,
                Name = "Restricted Counter",
                Code = "T-4",
                IsActive = true,
                IsDefault = true
            });
            db.CashSessions.Add(new CashSession
            {
                Id = 8102,
                BusinessId = 1,
                OutletId = 5002,
                TerminalId = 8002,
                BusinessDate = DateTime.UtcNow.Date,
                OpenedAt = DateTime.UtcNow.AddHours(-1),
                OpeningCashAmount = 25m,
                Status = "open",
                OpenedByUserId = 7001,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var client = CreatePosClient();
        using var active = await client.GetAsync(
            "/api/pos/cash-sessions/active?outletId=5002&terminalId=8002");
        using var summary = await client.GetAsync("/api/pos/cash-sessions/8102/summary");
        using var open = await client.PostAsJsonAsync("/api/pos/cash-sessions/open", new OpenCashSessionDto
        {
            OutletId = 5002,
            TerminalId = 8002,
            OpeningCashAmount = 100m
        });

        Assert.Equal(HttpStatusCode.Forbidden, active.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, open.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var seededDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(1, await seededDb.CashSessions.CountAsync(session =>
            session.OutletId == 5002 && session.TerminalId == 8002 && session.Status == "open"));
    }

    [Fact]
    public void CashSessionReadRoutes_RemainInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        Assert.Contains(OperationType.Get,
            swagger.Paths["/api/pos/cash-sessions/active"].Operations.Keys);
        Assert.Contains(OperationType.Post,
            swagger.Paths["/api/pos/cash-sessions/open"].Operations.Keys);
        Assert.Contains(OperationType.Get,
            swagger.Paths["/api/pos/cash-sessions/{id}/summary"].Operations.Keys);
    }

    [Fact]
    public async Task Terminals_UsesAuthorizedOutletAndPreservesActiveDefaultFirstOrdering()
    {
        await ResetAndSeedAsync(includeOpenSession: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet
            {
                Id = 5002,
                BusinessId = 1,
                Name = "Restricted outlet",
                Address = "Dhaka"
            });
            db.PosTerminals.AddRange(
                new PosTerminal
                {
                    Id = 8002,
                    OutletId = 5001,
                    Name = "A Counter",
                    Code = "T-2",
                    IsActive = true,
                    IsDefault = false
                },
                new PosTerminal
                {
                    Id = 8003,
                    OutletId = 5001,
                    Name = "Inactive Counter",
                    Code = "T-3",
                    IsActive = false,
                    IsDefault = false
                });
            await db.SaveChangesAsync();
        }

        using var client = CreatePosClient();
        using var response = await client.GetAsync("/api/pos/terminals?outletId=5001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await ReadApiEnvelope<PosTerminalDto[]>(response);
        Assert.NotNull(envelope?.Data);
        Assert.Equal(new long[] { 8001, 8002 }, envelope!.Data!.Select(terminal => terminal.Id));

        using var defaultOutletResponse = await client.GetAsync("/api/pos/terminals");
        Assert.Equal(HttpStatusCode.OK, defaultOutletResponse.StatusCode);

        using var unauthorizedResponse = await client.GetAsync("/api/pos/terminals?outletId=5002");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);
    }

    [Fact]
    public async Task OpenSummaryClose_Flow_Succeeds()
    {
        await ResetAndSeedAsync(includeOpenSession: false);
        using var client = CreatePosClient();

        var openResponse = await client.PostAsJsonAsync("/api/pos/cash-sessions/open", new OpenCashSessionDto
        {
            OutletId = 5001,
            TerminalId = 8001,
            OpeningCashAmount = 1000m,
            OpeningNotes = "Morning shift"
        });

        Assert.Equal(HttpStatusCode.OK, openResponse.StatusCode);
        var openEnvelope = await ReadApiEnvelope<CashSessionDto>(openResponse);
        Assert.NotNull(openEnvelope?.Data);
        var sessionId = openEnvelope!.Data!.Id;

        var activeResponse = await client.GetAsync("/api/pos/cash-sessions/active?outletId=5001&terminalId=8001");
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        var activeEnvelope = await ReadApiEnvelope<CashSessionDto?>(activeResponse);
        Assert.Equal(sessionId, activeEnvelope?.Data?.Id);

        var summaryResponse = await client.GetAsync($"/api/pos/cash-sessions/{sessionId}/summary");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        var summaryEnvelope = await ReadApiEnvelope<CashSessionCloseSummaryDto>(summaryResponse);
        Assert.NotNull(summaryEnvelope?.Data);
        Assert.Equal(1000m, summaryEnvelope!.Data!.OpeningCash);

        var closeResponse = await client.PostAsJsonAsync($"/api/pos/cash-sessions/{sessionId}/close", new CloseCashSessionDto
        {
            ActualClosingCashAmount = summaryEnvelope.Data.ExpectedClosingCash,
            ClosingNotes = null
        });

        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        var closeEnvelope = await ReadApiEnvelope<CashSessionDto>(closeResponse);
        Assert.NotNull(closeEnvelope?.Data);
        Assert.Equal("closed", closeEnvelope!.Data!.Status);

        var activeAfterClose = await client.GetAsync("/api/pos/cash-sessions/active?outletId=5001&terminalId=8001");
        Assert.Equal(HttpStatusCode.OK, activeAfterClose.StatusCode);
        var afterCloseEnvelope = await ReadApiEnvelope<CashSessionDto?>(activeAfterClose);
        Assert.Null(afterCloseEnvelope?.Data);
    }

    [Fact]
    public async Task Open_ReturnsExistingConflictContract_WhenSessionIsAlreadyOpen()
    {
        await ResetAndSeedAsync(includeOpenSession: true);
        using var client = CreatePosClient();

        using var response = await client.PostAsJsonAsync("/api/pos/cash-sessions/open", new OpenCashSessionDto
        {
            OutletId = 5001,
            TerminalId = 8001,
            OpeningCashAmount = 100m
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "CASH_SESSION_ALREADY_OPEN",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(1, await db.CashSessions.CountAsync(session =>
            session.OutletId == 5001 && session.TerminalId == 8001 && session.Status == "open"));
    }

    [Fact]
    public async Task Close_ReturnsBadRequest_WhenDifferenceExistsWithoutNote()
    {
        await ResetAndSeedAsync(includeOpenSession: true);
        using var client = CreatePosClient();

        var closeResponse = await client.PostAsJsonAsync("/api/pos/cash-sessions/8101/close", new CloseCashSessionDto
        {
            ActualClosingCashAmount = 1m,
            ClosingNotes = null
        });

        Assert.Equal(HttpStatusCode.BadRequest, closeResponse.StatusCode);

        var error = await ReadApiEnvelope<CashSessionDto>(closeResponse);
        Assert.NotNull(error);
        Assert.Contains("reason is required", error!.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Summary_ReturnsCalculatedTotals_ForSplitPaymentsRefundsAndCashMovements()
    {
        await ResetAndSeedAsync(includeOpenSession: true);
        await SeedSummaryDataAsync();
        using var client = CreatePosClient();

        var response = await client.GetAsync("/api/pos/cash-sessions/8101/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await ReadApiEnvelope<CashSessionCloseSummaryDto>(response);
        Assert.NotNull(envelope?.Data);

        var summary = envelope!.Data!;
        // Legacy rows without D14 evidence fall back to the allocated amount; a void with no recorded cash payout
        // keeps its cash in the drawer (no fabricated outflow); card/mobile never enter the drawer.
        Assert.Equal(120m, summary.CashSales);
        Assert.Equal(60m, summary.CardSales);
        Assert.Equal(50m, summary.MobileSales);
        Assert.Equal(0m, summary.CashRefunds);
        Assert.Equal(0m, summary.DrawerExpenses);
        Assert.Equal(620m, summary.ExpectedClosingCash);
    }

    private HttpClient CreatePosClient(
        string permissions = "sales.create,sales.view,cash_sessions.view,cash_sessions.open,cash_sessions.close")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7001");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Cashier");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync(bool includeOpenSession)
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
            Email = "cashier1@example.com",
            PasswordHash = "x",
            OutletId = 5001,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            IsActive = true
        });

        db.PosTerminals.Add(new PosTerminal
        {
            Id = 8001,
            OutletId = 5001,
            Name = "Counter-1",
            Code = "T-1",
            IsActive = true,
            IsDefault = true
        });

        if (includeOpenSession)
        {
            db.CashSessions.Add(new CashSession
            {
                Id = 8101,
                BusinessId = 1,
                OutletId = 5001,
                TerminalId = 8001,
                BusinessDate = DateTime.UtcNow.Date,
                OpenedAt = DateTime.UtcNow.AddHours(-2),
                OpeningCashAmount = 500m,
                Status = "open",
                OpenedByUserId = 7001,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedSummaryDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        db.Sales.Add(new Sale
        {
            Id = 9001,
            OutletId = 5001,
            TerminalId = 8001,
            CashSessionId = 8101,
            CashierId = 7001,
            SaleNumber = "INV-1",
            Status = "completed",
            PaymentMethod = "cash",
            TotalAmount = 100m,
            SaleDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Items = new List<SaleItem>(),
            Payments = new List<SalePayment>
            {
                new() { Method = "cash", Amount = 80m },
                new() { Method = "card", Amount = 20m }
            }
        });

        db.Sales.Add(new Sale
        {
            Id = 9002,
            OutletId = 5001,
            TerminalId = 8001,
            CashSessionId = 8101,
            CashierId = 7001,
            SaleNumber = "INV-2",
            Status = "completed",
            PaymentMethod = "mobile",
            TotalAmount = 50m,
            SaleDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Items = new List<SaleItem>(),
            Payments = new List<SalePayment>()
        });

        db.Sales.Add(new Sale
        {
            Id = 9003,
            OutletId = 5001,
            TerminalId = 8001,
            CashSessionId = 8101,
            CashierId = 7001,
            SaleNumber = "INV-3",
            Status = "voided",
            PaymentMethod = "cash",
            TotalAmount = 30m,
            SaleDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Items = new List<SaleItem>(),
            Payments = new List<SalePayment>()
        });

        db.Sales.Add(new Sale
        {
            Id = 9004,
            OutletId = 5001,
            TerminalId = 8001,
            CashSessionId = 8101,
            CashierId = 7001,
            SaleNumber = "INV-4",
            Status = "refunded",
            PaymentMethod = "card",
            TotalAmount = 50m,
            SaleDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Items = new List<SaleItem>(),
            Payments = new List<SalePayment>
            {
                new() { Method = "cash", Amount = 10m },
                new() { Method = "card", Amount = 40m }
            }
        });

        db.Accounts.Add(new Account
        {
            Id = 9101,
            BusinessId = 1,
            Name = "Main Cash",
            Type = "asset",
            Balance = 0m
        });

        await db.SaveChangesAsync();
    }

    private static async Task<ApiEnvelope<T>?> ReadApiEnvelope<T>(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }
}
