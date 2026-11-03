using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class P306StatementImportHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    public P306StatementImportHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task StatementEndpointsRequireDedicatedPermissionsAndAreTenantScoped()
    {
        await SeedAsync();
        using var missing = Client(1, "accounting.reports.general_ledger");
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/bank-statements/imports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/bank-statements/imports", new { bankAccountId = 501 })).StatusCode);

        using var tenantOne = Client(1, "accounting.statement_import.view");
        var one = await tenantOne.GetStringAsync("/api/accounting/bank-statements/imports");
        Assert.Contains("tenant-one.csv", one, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-two.csv", one, StringComparison.Ordinal);
        var oneDetail = await tenantOne.GetStringAsync(
            "/api/accounting/bank-statements/imports/2001");
        Assert.Contains("tenant-one.csv", oneDetail, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantOne.GetAsync("/api/accounting/bank-statements/imports/2002")).StatusCode);

        var oneLines = await tenantOne.GetStringAsync(
            "/api/accounting/bank-statements/lines?bankAccountId=501&dateFrom=2026-09-01&dateTo=2026-09-01");
        Assert.Contains("tenant-one line", oneLines, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-two line", oneLines, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportEndpointAcceptsMultipartCsvAndPreservesResponseContract()
    {
        await SeedAsync();
        using var client = Client(1, "accounting.statement_import.create");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("501"), "BankAccountId");
        var csv = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "transaction_date,description,debit,credit\n2026-09-01,Card settlement,12.50,\n"));
        csv.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        form.Add(csv, "File", "statement.csv");

        using var response = await client.PostAsync(
            "/api/accounting/bank-statements/imports",
            form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Statement imported successfully", body, StringComparison.Ordinal);
        Assert.Contains("statement.csv", body, StringComparison.Ordinal);
        Assert.Contains("\"acceptedRowCount\":1", body, StringComparison.Ordinal);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await db.BankStatementImports.Where(
            row => row.BusinessId == 1 && row.SourceFileName == "statement.csv").ToListAsync());
        var importId = await db.BankStatementImports
            .Where(row => row.BusinessId == 1 && row.SourceFileName == "statement.csv")
            .Select(row => row.Id)
            .SingleAsync();
        Assert.Single(await db.BankStatementLines
            .Where(row => row.BankStatementImportId == importId)
            .ToListAsync());
    }

    private HttpClient Client(long businessId, string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", "201");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync(); await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        db.ChartAccounts.AddRange(Account(101, 1), Account(201, 2));
        db.BankAccounts.AddRange(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "One Bank" }, new BankAccount { Id = 502, BusinessId = 2, ChartAccountId = 201, DisplayName = "Two Bank" });
        db.BankStatementImports.AddRange(new BankStatementImport { Id = 2001, BusinessId = 1, BankAccountId = 501, SourceFileName = "tenant-one.csv", SourceFileChecksum = "one", Status = BankStatementImportStatuses.Imported, ImportedAt = DateTime.UtcNow }, new BankStatementImport { Id = 2002, BusinessId = 2, BankAccountId = 502, SourceFileName = "tenant-two.csv", SourceFileChecksum = "two", Status = BankStatementImportStatuses.Imported, ImportedAt = DateTime.UtcNow });
        db.BankStatementLines.AddRange(
            new BankStatementLine
            {
                BusinessId = 1,
                BankAccountId = 501,
                BankStatementImportId = 2001,
                SourceRowNumber = 2,
                TransactionDate = new DateTime(2026, 9, 1),
                Description = "tenant-one line"
            },
            new BankStatementLine
            {
                BusinessId = 2,
                BankAccountId = 502,
                BankStatementImportId = 2002,
                SourceRowNumber = 2,
                TransactionDate = new DateTime(2026, 9, 1),
                Description = "tenant-two line"
            });
        await db.SaveChangesAsync();
    }

    private static ChartAccount Account(long id, long businessId) => new() { Id = id, BusinessId = businessId, AccountCode = id.ToString(), AccountName = $"Account {id}", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true };
}
