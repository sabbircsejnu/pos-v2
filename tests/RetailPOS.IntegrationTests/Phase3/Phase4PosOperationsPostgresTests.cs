using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Pos;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.Tests.Sales;

namespace RetailPOS.IntegrationTests.Phase3;

/// <summary>
/// Phase 4 server contracts behind the POS UI, on PostgreSQL through the production handlers and HTTP pipeline: the
/// salesperson (eligibility enforced at checkout, selector list and default), the receipt facts, the Daily Sales metric
/// for the outlet business date and the closing-summary PDF from the immutable close snapshot.
/// </summary>
public sealed partial class SaleFinancialPostingPostgresTests
{
    private const long SecondOutletId = 5003;
    private const long AssignedSeller = 7005;
    private const long InactiveSeller = 7006;

    [Fact]
    public Task Checkout_records_an_eligible_salesperson_and_rejects_every_ineligible_one() =>
        RunAsync(async (database, _) =>
        {
            await SeedPhase4UsersAsync(database);
            await OpenAsync(database);

            var credited = await SellWithSalespersonAsync(database, "p4-seller-ok", Manager);
            Assert.Equal(Manager, credited.SalespersonUserId);
            Assert.Equal("Manager", credited.SalespersonName);
            Assert.Equal(Cashier, credited.CashierId);
            Assert.Equal(Manager, await ScalarAsync<long>(database, $"SELECT salesperson_user_id FROM sales WHERE id = {credited.Id}"));

            var stockBefore = await ScalarAsync<int>(database, "SELECT quantity FROM inventories WHERE id = 9001");
            foreach (var (seller, key) in new[] { (OtherCashier, "p4-foreign"), (InactiveSeller, "p4-inactive"), (AssignedSeller, "p4-unassigned") })
            {
                var rejected = await Assert.ThrowsAsync<SaleCheckoutValidationException>(() => SellWithSalespersonAsync(database, key, seller));
                Assert.Equal(SaleCheckoutErrorCodes.SalespersonNotEligible, rejected.Code);
                Assert.Equal(0, await CountAsync(database, $"sales WHERE idempotency_key = '{key}'"));
            }

            Assert.Equal(stockBefore, await ScalarAsync<int>(database, "SELECT quantity FROM inventories WHERE id = 9001"));
            Assert.Equal(1, await CountAsync(database, "sale_financial_postings"));

            // An active assignment to the outlet makes the other outlet's user eligible.
            await ExecuteAsync(database, $"""
                INSERT INTO user_outlet_assignments (user_id, outlet_id, business_id, is_primary, is_active, created_at, updated_at)
                VALUES ({AssignedSeller}, {OutletId}, 1, false, true, now(), now())
                """);
            var assigned = await SellWithSalespersonAsync(database, "p4-assigned", AssignedSeller);
            Assert.Equal(AssignedSeller, assigned.SalespersonUserId);

            // Without a salesperson the sale is still valid and records none (older clients, mobile).
            var unrecorded = await SellAsync(database, "p4-none");
            Assert.True(await ScalarAsync<bool>(database, $"SELECT salesperson_user_id IS NULL FROM sales WHERE id = {unrecorded.Id}"));
        });

    [Fact]
    public Task The_salesperson_selector_lists_outlet_users_first_and_defaults_to_the_session_opener() =>
        RunAsync(async (database, app) =>
        {
            await SeedPhase4UsersAsync(database);
            await ExecuteAsync(database, $"""
                INSERT INTO user_outlet_assignments (user_id, outlet_id, business_id, is_primary, is_active, created_at, updated_at)
                VALUES ({AssignedSeller}, {OutletId}, 1, false, true, now(), now())
                """);
            await OpenAsync(database, cashier: Reviewer);
            var client = Client(app, "sales.create", userId: Manager);

            var withTerminal = await GetDataAsync(client, $"api/pos/salespersons?outletId={OutletId}&terminalId={Terminal1}");
            Assert.Equal(Reviewer, withTerminal.GetProperty("defaultSalespersonUserId").GetInt64());
            Assert.Equal("session_opener", withTerminal.GetProperty("defaultSource").GetString());
            var ids = withTerminal.GetProperty("salespersons").EnumerateArray().Select(p => p.GetProperty("userId").GetInt64()).ToArray();
            Assert.Equal([Cashier, Manager, Reviewer, AssignedSeller], ids);
            Assert.DoesNotContain(OtherCashier, ids);
            Assert.DoesNotContain(InactiveSeller, ids);

            var withoutTerminal = await GetDataAsync(client, $"api/pos/salespersons?outletId={OutletId}");
            Assert.Equal(Manager, withoutTerminal.GetProperty("defaultSalespersonUserId").GetInt64());
            Assert.Equal("current_user", withoutTerminal.GetProperty("defaultSource").GetString());

            Assert.Equal(HttpStatusCode.Forbidden,
                (await Client(app, "sales.view").GetAsync($"api/pos/salespersons?outletId={OutletId}")).StatusCode);
            var foreign = await Client(app, "sales.create", businessId: 2, userId: OtherCashier)
                .GetAsync($"api/pos/salespersons?outletId={OutletId}");
            Assert.False(foreign.IsSuccessStatusCode);
        });

    [Fact]
    public Task The_receipt_shows_salesperson_business_date_and_each_payment_with_tender_and_change() =>
        RunAsync(async (database, app) =>
        {
            var session = await OpenAsync(database);
            var sale = await SellWithSalespersonAsync(database, "p4-receipt", Manager);

            var receipt = await GetDataAsync(Client(app, "sales.view"), $"api/sales/{sale.Id}/receipt-document?mode=80mm");
            var info = receipt.GetProperty("saleInfo");
            Assert.Equal(sale.SaleNumber, info.GetProperty("invoiceNumber").GetString());
            Assert.Equal("Manager", info.GetProperty("salesperson").GetString());
            Assert.Equal("Cashier", info.GetProperty("cashier").GetString());
            Assert.Equal(session.BusinessDate.Date, info.GetProperty("businessDate").GetDateTime().Date);
            var payment = Assert.Single(receipt.GetProperty("payments").EnumerateArray());
            Assert.Equal("cash", payment.GetProperty("method").GetString());
            Assert.Equal(100m, payment.GetProperty("amount").GetDecimal());
            Assert.Equal(150m, payment.GetProperty("tendered").GetDecimal());
            Assert.Equal(50m, payment.GetProperty("change").GetDecimal());

            // Generating and reprinting never issue a second invoice number.
            Assert.Equal(HttpStatusCode.OK, (await Client(app, "sales.view").PostAsync($"api/sales/{sale.Id}/reprint", null)).StatusCode);
            Assert.Equal(1, await CountAsync(database, $"sales WHERE sale_number = '{sale.SaleNumber}'"));
            Assert.Equal(1, await CountAsync(database, "sales"));

            var pdf = await Client(app, "sales.view").GetAsync($"api/sales/{sale.Id}/receipt-pdf?mode=80mm");
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        });

    [Fact]
    public Task Daily_sales_count_completed_sales_of_the_outlet_business_date_and_stay_inside_the_Business() =>
        RunAsync(async (database, app) =>
        {
            var session = await OpenAsync(database);
            await SellAsync(database, "p4-day-1");
            var voided = await SellAsync(database, "p4-day-2", variantId: 3202);
            await VoidAsync(database, voided.Id);
            // A sale of another business date is not counted for today.
            await SellAsync(database, "p4-day-3");
            await ExecuteAsync(database, "UPDATE sales SET business_date = business_date - 1 WHERE idempotency_key = 'p4-day-3'");

            var viewer = Client(app, "cash_sessions.view");
            var day = await GetDataAsync(viewer, $"api/cash-sessions/business-day-sales?outletId={OutletId}");
            Assert.Equal(session.BusinessDate.Date, day.GetProperty("businessDate").GetDateTime().Date);
            Assert.Equal(1, day.GetProperty("completedCount").GetInt32());
            Assert.Equal(100m, day.GetProperty("completedTotal").GetDecimal());
            Assert.Equal(100m, day.GetProperty("completedNetOfTax").GetDecimal());
            Assert.Equal(1, day.GetProperty("voidedCount").GetInt32());
            Assert.Equal("Asia/Dhaka", day.GetProperty("timeZoneId").GetString());

            var yesterday = session.BusinessDate.Date.AddDays(-1).ToString("yyyy-MM-dd");
            var previous = await GetDataAsync(viewer, $"api/cash-sessions/business-day-sales?outletId={OutletId}&businessDate={yesterday}");
            Assert.Equal(1, previous.GetProperty("completedCount").GetInt32());

            var future = session.BusinessDate.Date.AddDays(2).ToString("yyyy-MM-dd");
            Assert.False((await viewer.GetAsync($"api/cash-sessions/business-day-sales?outletId={OutletId}&businessDate={future}")).IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Client(app, "sales.view").GetAsync($"api/cash-sessions/business-day-sales?outletId={OutletId}")).StatusCode);
            Assert.False((await Client(app, "cash_sessions.view", businessId: 2, userId: OtherCashier)
                .GetAsync($"api/cash-sessions/business-day-sales?outletId={OutletId}")).IsSuccessStatusCode);
        });

    [Fact]
    public Task The_closing_summary_pdf_is_served_from_the_close_snapshot_only_once_the_session_is_closed() =>
        RunAsync(async (database, app) =>
        {
            var session = await OpenAsync(database);
            await SellAsync(database, "p4-close-pdf");
            var viewer = Client(app, "cash_sessions.view");

            Assert.Equal(HttpStatusCode.Conflict, (await viewer.GetAsync($"api/cash-sessions/{session.Id}/closing-summary.pdf")).StatusCode);

            await CloseAsync(database, session.Id, 195m, "short by five");
            var response = await viewer.GetAsync($"api/cash-sessions/{session.Id}/closing-summary.pdf");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

            Assert.Equal(HttpStatusCode.Forbidden,
                (await Client(app, "sales.view").GetAsync($"api/cash-sessions/{session.Id}/closing-summary.pdf")).StatusCode);
            Assert.False((await Client(app, "cash_sessions.view", businessId: 2, userId: OtherCashier)
                .GetAsync($"api/cash-sessions/{session.Id}/closing-summary.pdf")).IsSuccessStatusCode);
        });

    // ── Phase 4 harness ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A second outlet of Business 1 with its own user (not assigned to the main outlet) and an inactive user.</summary>
    private static async Task SeedPhase4UsersAsync(PostgresTestDatabase database)
    {
        await using var db = Ctx(database);
        db.Outlets.Add(new Outlet { Id = SecondOutletId, BusinessId = 1, Name = "Second outlet", Address = "Sylhet", InvoicePrefix = "SYL" });
        await db.SaveChangesAsync();
        db.Users.Add(new User
        {
            Id = AssignedSeller, BusinessId = 1, Name = "Zed Assigned", Email = "assigned@phase4.test", PasswordHash = "x",
            OutletId = SecondOutletId, IsActive = true
        });
        db.Users.Add(new User
        {
            Id = InactiveSeller, BusinessId = 1, Name = "Inactive", Email = "inactive@phase4.test", PasswordHash = "x",
            OutletId = OutletId, IsActive = false
        });
        await db.SaveChangesAsync();
    }

    private static async Task<RetailPOS.Application.Sales.ReadModels.SaleReadModel> SellWithSalespersonAsync(
        PostgresTestDatabase database, string key, long salesperson)
    {
        await using var db = Ctx(database);
        var validator = new SaleCashSessionValidator(
            new CashSessionStore(db), Mock.Of<IAuditService>(), Mock.Of<ICashSessionExpiryPublisher>(),
            TimeProvider.System, NullLogger<SaleCashSessionValidator>.Instance);
        var cache = new Mock<IPosCacheService>();
        cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
        var handler = SaleCreationCommandTestFactory.Create(
            db, validator, new SaleInvoiceNumberAllocator(db), Mock.Of<IAuditService>(),
            new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db, NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance),
            cache.Object, Mock.Of<ISaleEventPublisher>(), businessId: 1);
        return await handler.Handle(
            new CreateSaleCommand(
                OutletId, Terminal1, null, [new CreateSaleLine(3201, 1, 100m, 0m, null, null)], 0m, 0m, "cash", [],
                Cashier, null, null, key, 150m, SalespersonUserId: salesperson),
            CancellationToken.None);
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{url} returned {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }
}
