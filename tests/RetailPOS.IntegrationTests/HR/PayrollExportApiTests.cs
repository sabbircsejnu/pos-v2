using System.Net;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed partial class PayrollRunApiTests
{
    [Fact]
    public async Task ExportEndpointsRequireExportAndConfidentialViewPermissionsAndAuditBeforeRelease()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await CreateApprovedRunAsync(factory, client);
        SetIdentity(client, 101, "");
        const string route = "/api/payroll/v1/runs/1/exports";

        SetPermissions(client, "payroll.export");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"{route}/csv")).StatusCode);
        SetPermissions(client, "payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"{route}/csv")).StatusCode);

        SetPermissions(client, "payroll.export,payroll.salary.view");
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.export.csv";
        using (var failedAudit = await client.GetAsync($"{route}/csv"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failedAudit.StatusCode);
            Assert.Null(failedAudit.Headers.CacheControl);
        }
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;

        using var csv = await client.GetAsync($"{route}/csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.True(csv.Headers.CacheControl?.Private);
        Assert.True(csv.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", csv.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.EndsWith("register.csv",
            csv.Content.Headers.ContentDisposition?.FileNameStar);
        var csvText = await csv.Content.ReadAsStringAsync();
        Assert.Contains("Employee Code", csvText);
        Assert.DoesNotContain("01700000000", csvText);
        Assert.DoesNotContain("StorageKey", csvText, StringComparison.OrdinalIgnoreCase);

        using var excel = await client.GetAsync($"{route}/excel");
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        Assert.True(excel.Headers.CacheControl?.Private);
        Assert.True(excel.Headers.CacheControl?.NoStore);
        using var pdf = await client.GetAsync($"{route}/pdf-summary");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.True(pdf.Headers.CacheControl?.Private);
        Assert.True(pdf.Headers.CacheControl?.NoStore);
        var pdfBytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 5));
        using var pdfDocument = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        var pdfText = string.Join('\n', pdfDocument.GetPages().Select(page => page.Text));

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"{route}/csv")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var result = await db.PayrollEmployeeResults.AsNoTracking()
            .SingleAsync(row => row.PayrollRunId == 1 && row.EmployeeId == 1);
        Assert.Contains(result.EmployeeCodeSnapshot, csvText);
        Assert.Contains(result.GrossEarnings!.Value.ToString(CultureInfo.InvariantCulture), csvText);
        Assert.Contains(result.TotalDeductions!.Value.ToString(CultureInfo.InvariantCulture), csvText);
        Assert.Contains(result.NetPay!.Value.ToString(CultureInfo.InvariantCulture), csvText);
        Assert.Contains($"BDT {result.GrossEarnings.Value.ToString("N2", CultureInfo.InvariantCulture)}", pdfText);
        Assert.Contains($"BDT {result.TotalDeductions.Value.ToString("N2", CultureInfo.InvariantCulture)}", pdfText);
        Assert.Contains($"BDT {result.NetPay.Value.ToString("N2", CultureInfo.InvariantCulture)}", pdfText);
        var componentAmounts = await db.PayrollEmployeeResultLines.AsNoTracking()
            .Where(row => row.PayrollEmployeeResultId == result.Id)
            .Select(row => row.Amount)
            .ToArrayAsync();
        Assert.NotEmpty(componentAmounts);
        foreach (var amount in componentAmounts)
            Assert.Contains(amount.ToString(CultureInfo.InvariantCulture), csvText);
        Assert.Equal(3, await db.AuditEvents.CountAsync(row =>
            row.ActionType.StartsWith("payroll.export.")));
    }

    [Fact]
    public async Task PayrollExportDeniedWhenEitherPayrollCapabilityIsDisabled()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await CreateApprovedRunAsync(factory, client);
        SetIdentity(client, 101, "payroll.export,payroll.salary.view");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var payroll = await db.BusinessFeatureSettings.SingleAsync(row =>
                row.BusinessId == 1 && row.FeatureKey == "payroll.enabled");
            payroll.IsEnabled = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/payroll/v1/runs/1/exports/csv")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var hr = await db.BusinessFeatureSettings.SingleAsync(row =>
                row.BusinessId == 1 && row.FeatureKey == "hr.enabled");
            hr.IsEnabled = false;
            var payroll = await db.BusinessFeatureSettings.SingleAsync(row =>
                row.BusinessId == 1 && row.FeatureKey == "payroll.enabled");
            payroll.IsEnabled = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/payroll/v1/runs/1/exports/csv")).StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await verifyDb.AuditEvents.Where(row =>
            row.ActionType.StartsWith("payroll.export.")).ToArrayAsync());
    }

    [Fact]
    public async Task PayrollExportDeniedForLocationLimitedBusinessRun()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await CreateApprovedRunAsync(factory, client);
        SetIdentity(client, 101, "payroll.export,payroll.salary.view");
        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        SetRole(client, "HR");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/payroll/v1/runs/1/exports/csv")).StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await verifyDb.AuditEvents.Where(row =>
            row.ActionType.StartsWith("payroll.export.")).ToArrayAsync());
    }

    [Fact]
    public async Task AdditionalEmployeeLocationDoesNotGrantExportAccessToPrimaryLocationRun()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await CreateApprovedRunAsync(factory, client, scopeType: "Location", scopeId: 10);
        await AddAdditionalEmployeeLocationAndScopeUserAsync(factory.Services);
        SetIdentity(client, 101, "payroll.export,payroll.salary.view");
        SetRole(client, "HR");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/payroll/v1/runs/1/exports/csv")).StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await verifyDb.AuditEvents.Where(row =>
            row.ActionType.StartsWith("payroll.export.")).ToArrayAsync());
    }
}
