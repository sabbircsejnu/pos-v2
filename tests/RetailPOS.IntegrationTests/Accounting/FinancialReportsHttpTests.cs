using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.Tests.Accounting;

public class FinancialReportsHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public FinancialReportsHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GeneralLedger_TrialBalance_And_ProfitLoss_AreDerived_From_Posted_Journals()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(201, "AccountsAdmin", "accounting.reports.general_ledger,accounting.reports.trial_balance,accounting.reports.profit_loss,accounting.journals.view");

        var ledgerResponse = await client.GetAsync("/api/accounting/reports/general-ledger?dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, ledgerResponse.StatusCode);
        var ledger = await ReadData<GeneralLedgerReportDto>(ledgerResponse);
        Assert.NotNull(ledger);
        Assert.NotEmpty(ledger!.Rows);
        Assert.Contains(ledger.Rows, x => x.AccountCode == "1000");
        Assert.Contains(ledger.Rows, x => x.AccountCode == "4000");

        long cashAccountId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            cashAccountId = await db.ChartAccounts
                .Where(account => account.BusinessId == 1 && account.AccountCode == "1000")
                .Select(account => account.Id)
                .SingleAsync();
        }

        var enhancedResponse = await client.GetAsync(
            $"/api/accounting/reports/enhanced-general-ledger?accountId={cashAccountId}&dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, enhancedResponse.StatusCode);
        var enhanced = await ReadData<EnhancedGeneralLedgerReportDto>(enhancedResponse);
        Assert.NotNull(enhanced);
        Assert.Equal("1000", enhanced!.AccountCode);

        var trialResponse = await client.GetAsync("/api/accounting/reports/trial-balance?dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, trialResponse.StatusCode);
        var trial = await ReadData<TrialBalanceReportDto>(trialResponse);
        Assert.NotNull(trial);
        Assert.Equal(trial!.TotalDebits, trial.TotalCredits);

        var pnlResponse = await client.GetAsync("/api/accounting/reports/profit-loss?dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, pnlResponse.StatusCode);
        var pnl = await ReadData<ProfitLossReportDto>(pnlResponse);
        Assert.NotNull(pnl);
        Assert.True(pnl!.Revenue >= 0);
        Assert.True(pnl.Expenses >= 0);
        Assert.Equal(pnl.Revenue - pnl.CostOfGoodsSold - pnl.Expenses, pnl.NetProfit);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync(
                "/api/accounting/reports/profit-loss/comparative?dateFrom=2026-07-01&dateTo=2026-07-31")).StatusCode);
        var comparativeResponse = await client.GetAsync(
            "/api/accounting/reports/profit-loss/comparative?dateFrom=2026-07-01&dateTo=2026-07-31&comparisonDateFrom=2026-06-01&comparisonDateTo=2026-06-30");
        Assert.Equal(HttpStatusCode.OK, comparativeResponse.StatusCode);
        var comparative = await ReadData<ComparativeProfitLossReportDto>(comparativeResponse);
        Assert.NotNull(comparative);
        Assert.Equal(
            comparative!.Current.Revenue - comparative.Current.CostOfGoodsSold - comparative.Current.Expenses,
            comparative.Current.NetProfit);
        Assert.Equal(
            comparative.Comparison.Revenue - comparative.Comparison.CostOfGoodsSold - comparative.Comparison.Expenses,
            comparative.Comparison.NetProfit);
    }

    [Fact]
    public async Task P206Reports_RequireTheirDedicatedPermissions()
    {
        using var client = CreateClient(201, "AccountsAdmin", "accounting.reports.general_ledger");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accounting/reports/cogs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accounting/reports/inventory-valuation")).StatusCode);
    }

    [Fact]
    public async Task CogsReport_UsesApplicationQueryAndPreservesConfiguredAccountResults()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var configuration = await db.AccountingConfigurations.SingleAsync(
                item => item.BusinessId == 1);
            configuration.CogsAccountId = 4000;
            configuration.InventoryAccountId = 1000;
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(201, "AccountsAdmin", "accounting.reports.cogs");
        var response = await client.GetAsync(
            "/api/accounting/reports/cogs?dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await ReadData<CogsReportDto>(response);
        Assert.NotNull(report);
        Assert.Equal(4000, report!.CogsAccountId);
        Assert.Equal(20m, report.TotalCogs);
        Assert.Equal(20m, report.GeneralLedgerBalance);
        Assert.Equal(0m, report.ReconciliationDifference);
    }

    [Fact]
    public async Task InventoryValuation_UsesApplicationQueryAndPreservesAsOfStockPool()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var configuration = await db.AccountingConfigurations.SingleAsync(
                item => item.BusinessId == 1);
            configuration.CogsAccountId = 4000;
            configuration.InventoryAccountId = 1000;
            db.Categories.Add(new Category
            {
                Id = 9101,
                BusinessId = 1,
                Name = "Valuation Category",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Products.Add(new Product
            {
                Id = 9101,
                BusinessId = 1,
                Name = "Valuation Product",
                ProductCode = "VAL-1",
                CategoryId = 9101,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.ProductVariants.Add(new ProductVariant
            {
                Id = 9101,
                BusinessId = 1,
                ProductId = 9101,
                Name = "Default",
                Sku = "VAL-1-DEFAULT",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.StockLedgers.Add(new StockLedger
            {
                Id = 9101,
                VariantId = 9101,
                LocationId = 11,
                LocationType = "outlet",
                QtyIn = 2,
                BalanceAfter = 2,
                UnitCost = 5m,
                MovementValue = 10m,
                IsCostValuationReliable = true,
                BusinessDate = new DateTime(2026, 7, 10),
                MovementSequence = 1,
                TransactionType = "Receipt",
                ReferenceType = "Test",
                ReferenceId = 9101,
                CreatedAt = new DateTime(2026, 7, 10)
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(
            201,
            "AccountsAdmin",
            "accounting.reports.inventory_valuation");
        var response = await client.GetAsync(
            "/api/accounting/reports/inventory-valuation?dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await ReadData<InventoryValuationReportDto>(response);
        Assert.NotNull(report);
        Assert.Equal(new DateTime(2026, 7, 31), report!.AsOfDate);
        Assert.Equal(2m, report.TotalQuantity);
        Assert.Equal(10m, report.TotalInventoryValue);
        Assert.Equal("VAL-1-DEFAULT", Assert.Single(report.Rows).Sku);
        Assert.Empty(report.UnvaluedMovements);
    }

    [Fact]
    public async Task CashBook_RequiresDedicatedPermission()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(201, "AccountsAdmin", "accounting.reports.general_ledger");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accounting/reports/cash-book")).StatusCode);

        using var authorized = CreateClient(201, "AccountsAdmin", "accounting.reports.cash_book");
        var response = await authorized.GetAsync(
            "/api/accounting/reports/cash-book?dateFrom=2026-07-01&dateTo=2026-07-31&accountSlots=cash");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await ReadData<CashBookReportDto>(response);
        var cash = Assert.Single(report!.Accounts);
        Assert.Equal("cash", cash.AccountSlot);
        Assert.Equal(40m, cash.ClosingBalance);
        Assert.Equal(0m, cash.ReconciliationDifference);
        Assert.Equal(3, cash.Rows.Count);
    }

    [Fact]
    public async Task CashFlow_RequiresPermission_ReturnsReport_AndExportsSameScope()
    {
        await ResetAndSeedAsync();
        using var missing = CreateClient(201, "AccountsAdmin", "accounting.reports.cash_book");
        using var authorized = CreateClient(201, "AccountsAdmin", "accounting.reports.cash_flow");

        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reports/cash-flow?dateFrom=2026-07-01&dateTo=2026-07-31")).StatusCode);

        var response = await authorized.GetAsync("/api/accounting/reports/cash-flow?dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await ReadData<CashFlowReportDto>(response);
        Assert.NotNull(report);
        Assert.Equal(40m, report!.UnclassifiedNetMovement);
        Assert.Contains(report.Sections, x => x.ActivityType == "Operating");
        Assert.Contains(report.Sections, x => x.ActivityType == "Unclassified");

        var comparativeResponse = await authorized.GetAsync("/api/accounting/reports/cash-flow/comparative?dateFrom=2026-07-01&dateTo=2026-07-31&comparisonDateFrom=2026-06-01&comparisonDateTo=2026-06-30");
        Assert.Equal(HttpStatusCode.OK, comparativeResponse.StatusCode);
        var comparative = await ReadData<ComparativeCashFlowReportDto>(comparativeResponse);
        Assert.NotNull(comparative);
        Assert.Equal(report.TotalExternalNetMovement, comparative!.Current.TotalExternalNetMovement);

        var exportResponse = await authorized.GetAsync("/api/accounting/reports/cash-flow/export?dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        var csv = await exportResponse.Content.ReadAsStringAsync();
        Assert.Contains("Current Cash Flow Statement", csv);
        Assert.Contains("Unclassified", csv);
    }

    [Fact]
    public async Task ProfitabilityReports_RequirePermission_ReturnReports_AndExportAuthoritativeResults()
    {
        await ResetAndSeedAsync();
        await SeedProductContributionEvidenceAsync();
        using var missing = CreateClient(201, "AccountsAdmin", "accounting.reports.profit_loss");
        using var authorized = CreateClient(201, "AccountsAdmin", "accounting.reports.profitability");

        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reports/business-profitability?dateFrom=2026-07-01&dateTo=2026-07-31")).StatusCode);

        var businessResponse = await authorized.GetAsync("/api/accounting/reports/business-profitability?dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, businessResponse.StatusCode);
        var business = await ReadData<BusinessProfitabilityReportDto>(businessResponse);
        Assert.NotNull(business);
        Assert.Equal(business!.NetProfit, business.AuthoritativePnlNetProfit);

        var outletResponse = await authorized.GetAsync("/api/accounting/reports/outlet-profitability?dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        var outlet = await ReadData<OutletProfitabilityReportDto>(outletResponse);
        Assert.NotNull(outlet);
        Assert.Equal(0m, outlet!.ReconciliationDifference);

        var productResponse = await authorized.GetAsync("/api/accounting/reports/product-profitability?dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, productResponse.StatusCode);
        var product = await ReadData<ProductContributionReportDto>(productResponse);
        Assert.NotNull(product);
        Assert.Contains("shared expenses", product!.ContributionBasis, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(product.Products);

        var comparativeProductResponse = await authorized.GetAsync(
            "/api/accounting/reports/product-profitability/comparative?dateFrom=2026-07-01&dateTo=2026-07-31&comparisonDateFrom=2026-06-01&comparisonDateTo=2026-06-30");
        Assert.Equal(HttpStatusCode.OK, comparativeProductResponse.StatusCode);

        var comparativeResponse = await authorized.GetAsync("/api/accounting/reports/business-profitability/comparative?dateFrom=2026-07-01&dateTo=2026-07-31&comparisonDateFrom=2026-06-01&comparisonDateTo=2026-06-30");
        Assert.Equal(HttpStatusCode.OK, comparativeResponse.StatusCode);

        var comparativeOutletResponse = await authorized.GetAsync(
            "/api/accounting/reports/outlet-profitability/comparative?dateFrom=2026-07-01&dateTo=2026-07-31&comparisonDateFrom=2026-06-01&comparisonDateTo=2026-06-30");
        Assert.Equal(HttpStatusCode.OK, comparativeOutletResponse.StatusCode);

        foreach (var reportType in new[] { "business", "outlet" })
        {
            var reportExport = await authorized.GetAsync(
                $"/api/accounting/reports/profitability/export?reportType={reportType}&dateFrom=2026-07-01&dateTo=2026-07-31");
            Assert.Equal(HttpStatusCode.OK, reportExport.StatusCode);
            Assert.Equal("text/csv", reportExport.Content.Headers.ContentType?.MediaType);
        }

        foreach (var reportType in new[] { "product", "category" })
        {
            var exportResponse = await authorized.GetAsync(
                $"/api/accounting/reports/profitability/export?reportType={reportType}&dateFrom=2026-07-01&dateTo=2026-07-31&comparisonDateFrom=2026-06-01&comparisonDateTo=2026-06-30");
            Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
            var csv = await exportResponse.Content.ReadAsStringAsync();
            Assert.Contains("Product Contribution", csv);
            Assert.Contains("Unallocated Revenue", csv);
            Assert.Contains("Comparison Product Contribution", csv);
        }
    }

    [Fact]
    public async Task P301Reports_EnforcePermissionsAndLocationAuthorization()
    {
        await ResetAndSeedAsync();
        using var noPermission = CreateClient(201, "AccountsAdmin", "accounting.reports.general_ledger");
        using var balanceSheetUser = CreateClient(201, "AccountsAdmin", "accounting.reports.balance_sheet");

        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.GetAsync("/api/accounting/reports/balance-sheet?asOfDate=2026-07-31")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await balanceSheetUser.GetAsync("/api/accounting/reports/enhanced-general-ledger?accountId=1000")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await balanceSheetUser.GetAsync("/api/accounting/reports/balance-sheet?asOfDate=2026-07-31&locationId=999&locationType=outlet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await balanceSheetUser.GetAsync("/api/accounting/reports/balance-sheet?asOfDate=2026-07-31&locationId=999&locationType=warehouse")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var configuration = await db.AccountingConfigurations.SingleAsync(item => item.BusinessId == 1);
            var retainedEarnings = Account(
                await db.ChartAccounts.Where(account => account.BusinessId == 1)
                    .MaxAsync(account => account.Id) + 1,
                "EQ-TEST",
                "Retained Earnings",
                ChartAccountTypes.Equity,
                NormalBalanceTypes.Credit);
            db.ChartAccounts.Add(retainedEarnings);
            await db.SaveChangesAsync();
            configuration.RetainedEarningsAccountId = retainedEarnings.Id;
            await db.SaveChangesAsync();
        }

        var balanceSheetResponse = await balanceSheetUser.GetAsync(
            "/api/accounting/reports/balance-sheet?asOfDate=2026-07-31");
        Assert.True(
            balanceSheetResponse.StatusCode == HttpStatusCode.OK,
            await balanceSheetResponse.Content.ReadAsStringAsync());
        var balanceSheet = await ReadData<BalanceSheetReportDto>(balanceSheetResponse);
        Assert.NotNull(balanceSheet);
        Assert.Equal(new DateTime(2026, 7, 31), balanceSheet!.AsOfDate);
        Assert.Equal("Assets", balanceSheet.Assets.Name);
        Assert.Equal("Liabilities", balanceSheet.Liabilities.Name);
        Assert.Equal("Equity", balanceSheet.Equity.Name);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await balanceSheetUser.GetAsync(
                "/api/accounting/reports/balance-sheet/comparative?asOfDate=2026-07-31")).StatusCode);
        var comparativeResponse = await balanceSheetUser.GetAsync(
            "/api/accounting/reports/balance-sheet/comparative?asOfDate=2026-07-31&comparisonAsOfDate=2025-12-31");
        Assert.Equal(HttpStatusCode.OK, comparativeResponse.StatusCode);
        var comparative = await ReadData<ComparativeBalanceSheetReportDto>(comparativeResponse);
        Assert.NotNull(comparative);
        Assert.Equal(new DateTime(2026, 7, 31), comparative!.Current.AsOfDate);
        Assert.Equal(new DateTime(2025, 12, 31), comparative.Comparison.AsOfDate);

        using var apNoPermission = CreateClient(201, "AccountsAdmin", "accounting.reports.general_ledger");
        using var apUser = CreateClient(201, "AccountsAdmin", "accounting.reports.ap_aging");
        Assert.Equal(HttpStatusCode.Forbidden, (await apNoPermission.GetAsync("/api/accounting/reports/ap-aging?asOfDate=2026-07-31")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apUser.GetAsync("/api/accounting/reports/ap-aging?asOfDate=2026-07-31&locationId=999&locationType=outlet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apUser.GetAsync("/api/accounting/reports/ap-aging?asOfDate=2026-07-31&locationId=999&locationType=warehouse")).StatusCode);
    }

    [Fact]
    public async Task ApSubledgerAndAging_PreservePermissionsAndResponseEnvelope()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "AP HTTP Supplier" });
            db.ChartAccounts.AddRange(
                Account(5000, "5000", "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
                Account(5001, "5001", "Supplier Advance", ChartAccountTypes.Liability, NormalBalanceTypes.Credit));
            var configuration = await db.AccountingConfigurations.SingleAsync(item => item.BusinessId == 1);
            configuration.SupplierPayableAccountId = 5000;
            configuration.SupplierAdvanceAccountId = 5001;
            await db.SaveChangesAsync();
        }

        using var missing = CreateClient(201, "AccountsAdmin", "accounting.reports.general_ledger");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await missing.GetAsync("/api/accounting/reports/ap-subledger?asOfDate=2026-07-31")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await missing.GetAsync("/api/accounting/reports/ap-aging?asOfDate=2026-07-31")).StatusCode);

        using var authorized = CreateClient(
            201,
            "AccountsAdmin",
            "accounting.reports.ap_subledger,accounting.reports.ap_aging,accounting.reports.supplier_statement,accounting.reports.ap_reconciliation");
        var subledgerResponse = await authorized.GetAsync(
            "/api/accounting/reports/ap-subledger?asOfDate=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, subledgerResponse.StatusCode);
        var subledger = await ReadData<ApSubledgerReportDto>(subledgerResponse);
        Assert.NotNull(subledger);
        Assert.Equal(new DateTime(2026, 7, 31), subledger!.AsOfDate);
        Assert.Empty(subledger.Rows);
        Assert.NotNull(subledger.SupplierTotals);
        Assert.NotNull(subledger.Exceptions);

        var agingResponse = await authorized.GetAsync(
            "/api/accounting/reports/ap-aging?asOfDate=2026-07-31&agingBucket=Current%20%2F%20Not%20Due");
        Assert.Equal(HttpStatusCode.OK, agingResponse.StatusCode);
        var aging = await ReadData<ApAgingReportDto>(agingResponse);
        Assert.NotNull(aging);
        Assert.Equal(new DateTime(2026, 7, 31), aging!.AsOfDate);
        Assert.Empty(aging.Rows);
        Assert.NotNull(aging.SupplierTotals);
        Assert.NotNull(aging.Exceptions);

        var statementResponse = await authorized.GetAsync(
            "/api/accounting/reports/supplier-statement?supplierId=10&dateFrom=2026-07-01&dateTo=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, statementResponse.StatusCode);
        var statement = await ReadData<SupplierStatementReportDto>(statementResponse);
        Assert.NotNull(statement);
        Assert.Equal(10, statement!.SupplierId);
        Assert.Equal("AP HTTP Supplier", statement.SupplierName);
        Assert.Empty(statement.Rows);
        Assert.Equal(0m, statement.ClosingBalance);

        var payableReconciliationResponse = await authorized.GetAsync(
            "/api/accounting/reports/ap-reconciliation?asOfDate=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, payableReconciliationResponse.StatusCode);
        var payableReconciliation = await ReadData<ApReconciliationReportDto>(payableReconciliationResponse);
        Assert.NotNull(payableReconciliation);
        Assert.Equal("supplier_payable", payableReconciliation!.AccountSlot);
        Assert.Equal(0m, payableReconciliation.ReconciliationDifference);

        var advanceReconciliationResponse = await authorized.GetAsync(
            "/api/accounting/reports/supplier-advance-reconciliation?asOfDate=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, advanceReconciliationResponse.StatusCode);
        var advanceReconciliation = await ReadData<ApReconciliationReportDto>(advanceReconciliationResponse);
        Assert.NotNull(advanceReconciliation);
        Assert.Equal("supplier_advance", advanceReconciliation!.AccountSlot);
        Assert.Equal(0m, advanceReconciliation.ReconciliationDifference);
    }

    [Fact]
    public async Task VatReports_RequirePermissionAndRejectUnauthorizedLocations()
    {
        await ResetAndSeedAsync();
        var endpoints = new[] { "vat-summary", "vat-details", "vat-reconciliation", "vat-exceptions" };
        using var missing = CreateClient(201, "AccountsAdmin", "accounting.reports.general_ledger");
        foreach (var endpoint in endpoints)
            Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync($"/api/accounting/reports/{endpoint}?dateFrom=2026-07-01&dateTo=2026-07-31")).StatusCode);

        using var authorized = CreateClient(201, "AccountsAdmin", "accounting.reports.vat");
        foreach (var endpoint in endpoints)
            Assert.Equal(HttpStatusCode.Forbidden, (await authorized.GetAsync($"/api/accounting/reports/{endpoint}?dateFrom=2026-07-01&dateTo=2026-07-31&locationId=999&locationType=outlet")).StatusCode);
        foreach (var endpoint in endpoints)
            Assert.Equal(HttpStatusCode.Forbidden, (await authorized.GetAsync($"/api/accounting/reports/{endpoint}?dateFrom=2026-07-01&dateTo=2026-07-31&locationId=999&locationType=warehouse")).StatusCode);

        using var otherBusiness = CreateClient(201, "AccountsAdmin", "accounting.reports.vat");
        otherBusiness.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        otherBusiness.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        foreach (var endpoint in endpoints)
        {
            var response = await otherBusiness.GetAsync($"/api/accounting/reports/{endpoint}?dateFrom=2026-07-01&dateTo=2026-07-31");
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
            Assert.DoesNotContain("Monthly revenue", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("seed-1", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void VatReportRoutesAndQueryParametersRemainInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var expectedRoutes = new[]
        {
            "/api/accounting/reports/vat-summary",
            "/api/accounting/reports/vat-details",
            "/api/accounting/reports/vat-reconciliation",
            "/api/accounting/reports/vat-exceptions"
        };
        var expectedParameters = new[]
        {
            "DateFrom",
            "DateTo",
            "OutletId",
            "WarehouseId",
            "VatDirection",
            "SourceType",
            "SourceReference",
            "TaxCode",
            "VatRate",
            "LocationId",
            "LocationType"
        };

        foreach (var route in expectedRoutes)
        {
            var operation = swagger.Paths[route].Operations[OperationType.Get];
            Assert.Equal(expectedParameters, operation.Parameters.Select(parameter => parameter.Name));
            Assert.All(operation.Parameters, parameter =>
                Assert.Equal(ParameterLocation.Query, parameter.In));
        }
    }

    private HttpClient CreateClient(long userId, string role, string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Financial Reports Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.Users.Add(new User
        {
            Id = 201,
            BusinessId = 1,
            Name = "Reporter",
            Email = "reporter@example.test",
            PasswordHash = "test",
            OutletId = 11,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.ChartAccounts.AddRange(
            Account(1000, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1100, "1010", "Petty Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1200, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2000, "2000", "Revenue", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(3000, "3000", "Expense", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(4000, "4000", "Cost of Goods Sold", ChartAccountTypes.CostOfGoodsSold, NormalBalanceTypes.Debit)
        );

        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            CashAccountId = 1000,
            PettyCashAccountId = 1100,
            DefaultBankAccountId = 1200,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            ManualJournalWorkflowMode = ManualJournalWorkflowModes.DraftPost,
            ManualJournalAttachmentMaxBytes = 10 * 1024 * 1024,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.FinancialYears.Add(new FinancialYear
        {
            Id = 9001,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "FY26",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 9002,
            FinancialYearId = 9001,
            PeriodNumber = 7,
            Name = "July",
            StartDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var journal = new JournalEntry
        {
            BusinessId = 1,
            JournalType = AccountingJournalTypes.ManualJournal,
            TransactionDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            PostingDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            BusinessDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            FinancialYearId = 9001,
            AccountingPeriodId = 9002,
            Description = "Monthly revenue",
            Status = JournalEntryStatuses.Posted,
            ReferenceType = "ManualJournal",
            ReferenceId = "seed-1",
            OutletId = 11,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = 1000, DebitAmount = 100m, CreditAmount = 0m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 },
                new() { AccountId = 2000, DebitAmount = 0m, CreditAmount = 100m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 }
            }
        };

        var expenseJournal = new JournalEntry
        {
            BusinessId = 1,
            JournalType = AccountingJournalTypes.ManualJournal,
            TransactionDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            PostingDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            BusinessDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            FinancialYearId = 9001,
            AccountingPeriodId = 9002,
            Description = "Monthly expense",
            Status = JournalEntryStatuses.Posted,
            ReferenceType = "ManualJournal",
            ReferenceId = "seed-2",
            OutletId = 11,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = 3000, DebitAmount = 40m, CreditAmount = 0m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 },
                new() { AccountId = 1000, DebitAmount = 0m, CreditAmount = 40m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 }
            }
        };

        var cogsJournal = new JournalEntry
        {
            BusinessId = 1,
            JournalType = AccountingJournalTypes.ManualJournal,
            TransactionDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            PostingDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            BusinessDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            FinancialYearId = 9001,
            AccountingPeriodId = 9002,
            Description = "COGS entry",
            Status = JournalEntryStatuses.Posted,
            ReferenceType = "ManualJournal",
            ReferenceId = "seed-3",
            OutletId = 11,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = 4000, DebitAmount = 20m, CreditAmount = 0m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 },
                new() { AccountId = 1000, DebitAmount = 0m, CreditAmount = 20m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 }
            }
        };

        db.JournalEntries.AddRange(journal, expenseJournal, cogsJournal);
        await db.SaveChangesAsync();
    }

    private async Task SeedProductContributionEvidenceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Categories.Add(new Category { Id = 4101, BusinessId = 1, Name = "Contribution Category", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Products.Add(new Product { Id = 5101, BusinessId = 1, Name = "Contribution Product", ProductCode = "CP-1", CategoryId = 4101, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.ProductVariants.Add(new ProductVariant { Id = 6101, BusinessId = 1, ProductId = 5101, Name = "Default", Sku = "CP-1-DEFAULT", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        var item = new SaleItem { Id = 7101, VariantId = 6101, Quantity = 2, UnitPrice = 50m, Subtotal = 100m };
        item.CaptureHistoricalUnitCost(20m);
        db.Sales.Add(new Sale { Id = 8101, SaleNumber = "S-CONTRIB-1", OutletId = 11, SaleDate = new DateTime(2026, 7, 22), Status = "completed", CashierId = 201, Items = [item] });
        db.JournalEntries.Add(new JournalEntry
        {
            BusinessId = 1,
            JournalType = "SalePosting",
            TransactionDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
            PostingDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
            BusinessDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
            FinancialYearId = 9001,
            AccountingPeriodId = 9002,
            Description = "Contribution sale",
            Status = JournalEntryStatuses.Posted,
            ReferenceType = "Sale",
            ReferenceId = "8101",
            OutletId = 11,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = 1000, DebitAmount = 100m, CreditAmount = 0m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 },
                new() { AccountId = 2000, DebitAmount = 0m, CreditAmount = 100m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 },
                new() { AccountId = 4000, DebitAmount = 40m, CreditAmount = 0m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 },
                new() { AccountId = 1000, DebitAmount = 0m, CreditAmount = 40m, OutletId = 11, CreatedAt = DateTime.UtcNow, CreatedBy = 201 }
            }
        });
        await db.SaveChangesAsync();
    }

    private static async Task<T?> ReadData<T>(HttpResponseMessage response) where T : class
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return payload?.Data;
    }

    private static ChartAccount Account(long id, string code, string name, string type, string normalBalance)
    {
        return new ChartAccount
        {
            Id = id,
            BusinessId = 1,
            AccountCode = code,
            AccountName = name,
            AccountType = type,
            NormalBalanceType = normalBalance,
            AllowManualPosting = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class GeneralLedgerReportDto
    {
        public List<GeneralLedgerRowDto> Rows { get; set; } = new();
        public ReportMetadataDto Metadata { get; set; } = new();
    }

    private sealed class GeneralLedgerRowDto
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string JournalNumber { get; set; } = string.Empty;
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal RunningBalance { get; set; }
    }

    private sealed class TrialBalanceReportDto
    {
        public List<TrialBalanceRowDto> Rows { get; set; } = new();
        public decimal TotalDebits { get; set; }
        public decimal TotalCredits { get; set; }
        public ReportMetadataDto Metadata { get; set; } = new();
    }

    private sealed class TrialBalanceRowDto
    {
        public string AccountCode { get; set; } = string.Empty;
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
    }

    private sealed class ProfitLossReportDto
    {
        public decimal Revenue { get; set; }
        public decimal CostOfGoodsSold { get; set; }
        public decimal Expenses { get; set; }
        public decimal NetProfit { get; set; }
        public ReportMetadataDto Metadata { get; set; } = new();
    }

    private sealed class ReportMetadataDto
    {
        public string Source { get; set; } = string.Empty;
    }

    private sealed class CashFlowReportDto
    {
        public decimal UnclassifiedNetMovement { get; set; }
        public decimal TotalExternalNetMovement { get; set; }
        public List<CashFlowSectionDto> Sections { get; set; } = new();
    }

    private sealed class CashFlowSectionDto
    {
        public string ActivityType { get; set; } = string.Empty;
    }

    private sealed class ComparativeCashFlowReportDto
    {
        public CashFlowReportDto Current { get; set; } = new();
        public CashFlowReportDto Comparison { get; set; } = new();
    }

    private sealed class BusinessProfitabilityReportDto
    {
        public decimal NetProfit { get; set; }
        public decimal AuthoritativePnlNetProfit { get; set; }
    }

    private sealed class OutletProfitabilityReportDto
    {
        public decimal ReconciliationDifference { get; set; }
    }

    private sealed class ProductContributionReportDto
    {
        public string ContributionBasis { get; set; } = string.Empty;
        public List<object> Products { get; set; } = new();
    }
}
