using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class FinancialPeriodAuditTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public FinancialPeriodAuditTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LegacyFinancialYearCloseRoutesAreBlocked_AndPeriodStatusChangesWriteAccountsAuditEvents()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.financial_years.view,accounting.financial_years.manage,accounting.periods.view,accounting.periods.close,accounting.periods.reopen");

        var closeYear = await client.PostAsJsonAsync("/api/accounting/financial-years/3001/close", new { reason = "Year-end close" });
        Assert.Equal(HttpStatusCode.BadRequest, closeYear.StatusCode);

        var reopenYear = await client.PostAsJsonAsync("/api/accounting/financial-years/3001/reopen", new { reason = "Post-close adjustment approved" });
        Assert.Equal(HttpStatusCode.BadRequest, reopenYear.StatusCode);

        var lockPeriod = await client.PostAsJsonAsync("/api/accounting/periods/3101/lock", new { reason = "Freeze postings" });
        Assert.Equal(HttpStatusCode.OK, lockPeriod.StatusCode);

        var reopenPeriod = await client.PostAsJsonAsync("/api/accounting/periods/3101/reopen", new { reason = "Adjustment window" });
        Assert.Equal(HttpStatusCode.OK, reopenPeriod.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var auditRows = await db.AuditEvents
            .AsNoTracking()
            .Where(x => x.Module == AuditModule.Accounts)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        Assert.NotEmpty(auditRows);
        Assert.Contains(auditRows, x => x.ActionSummary.Contains("Accounting period status changed: 3101", StringComparison.Ordinal));
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "RM03 Audit Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "RM03 Audit User",
            Email = "rm03-audit@example.test",
            PasswordHash = "test",
            OutletId = 11,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.FinancialYears.Add(new FinancialYear
        {
            Id = 3001,
            BusinessId = 1,
            YearCode = "FY2026",
            Name = "FY 2026",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 3101,
            FinancialYearId = 3001,
            PeriodNumber = 1,
            Name = "January",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }
}
