using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class ManualJournalAuditTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public ManualJournalAuditTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Submit_Approve_And_Attachment_Actions_Write_Accounts_AuditEvents()
    {
        await ResetAndSeedAsync();
        using var maker = CreateClient(101, "AccountsAdmin", "accounting.manual_journals.create,accounting.manual_journals.submit,accounting.manual_journals.update,accounting.manual_journals.view");
        using var checker = CreateClient(102, "AccountsAdmin", "accounting.manual_journals.approve,accounting.manual_journals.post,accounting.manual_journals.view");

        var createResponse = await maker.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc),
            description = "Audit manual journal",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 33m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 33m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiEnvelope<JournalEntryResponse>>();
        Assert.NotNull(created?.Data);

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("audit attachment"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "File", "audit.pdf");
        var uploadResponse = await maker.PostAsync($"/api/accounting/journals/{created!.Data!.Id}/attachments", form);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        var attachmentsResponse = await maker.GetAsync($"/api/accounting/journals/{created.Data.Id}/attachments");
        var attachments = await attachmentsResponse.Content.ReadFromJsonAsync<ApiEnvelope<List<JournalAttachmentResponse>>>();
        var attachmentId = Assert.Single(attachments!.Data!).Id;

        var downloadResponse = await maker.GetAsync(
            $"/api/accounting/journals/{created.Data.Id}/attachments/{attachmentId}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        var deleteResponse = await maker.DeleteAsync(
            $"/api/accounting/journals/{created.Data.Id}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var submitResponse = await maker.PostAsync($"/api/accounting/journals/{created.Data.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var approveResponse = await checker.PostAsJsonAsync($"/api/accounting/journals/{created.Data.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var auditRows = await db.AuditEvents
            .AsNoTracking()
            .Where(x => x.Module == AuditModule.Accounts)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        var uploadAudit = Assert.Single(auditRows, x => x.ActionSummary.Contains("Journal attachment uploaded", StringComparison.Ordinal));
        var downloadAudit = Assert.Single(auditRows, x => x.ActionSummary.Contains("Journal attachment downloaded", StringComparison.Ordinal));
        var deleteAudit = Assert.Single(auditRows, x => x.ActionSummary.Contains("Journal attachment deleted", StringComparison.Ordinal));
        Assert.Contains(auditRows, x => x.ActionSummary.Contains("Journal submitted", StringComparison.Ordinal));
        Assert.Contains(auditRows, x => x.ActionSummary.Contains("Journal approved", StringComparison.Ordinal));

        foreach (var attachmentAudit in new[] { uploadAudit, downloadAudit, deleteAudit })
        {
            Assert.Equal(1L, attachmentAudit.BusinessId);
            Assert.Equal(101L, attachmentAudit.RealUserId);
            Assert.NotEqual(default, attachmentAudit.CreatedAt);
        }

        var submitAudit = Assert.Single(auditRows, x => x.ActionSummary.Contains("Journal submitted", StringComparison.Ordinal));
        Assert.Equal(1L, submitAudit.BusinessId);
        Assert.Equal(101L, submitAudit.RealUserId);
        Assert.NotEqual(default, submitAudit.CreatedAt);

        var approveAudit = Assert.Single(auditRows, x => x.ActionSummary.Contains("Journal approved", StringComparison.Ordinal));
        Assert.Equal(1L, approveAudit.BusinessId);
        Assert.Equal(102L, approveAudit.RealUserId);
        Assert.NotEqual(default, approveAudit.CreatedAt);
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

        db.Businesses.Add(new Business { Id = 1, Name = "Manual Audit Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.Users.AddRange(
            new User { Id = 101, BusinessId = 1, Name = "Maker", Email = "maker-audit@example.test", PasswordHash = "test", OutletId = 11, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Id = 102, BusinessId = 1, Name = "Checker", Email = "checker-audit@example.test", PasswordHash = "test", OutletId = 11, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit)
        );

        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            ManualJournalWorkflowMode = ManualJournalWorkflowModes.ApprovalRequired,
            ManualJournalAllowSelfApproval = false,
            ManualJournalAttachmentMaxBytes = 10 * 1024 * 1024,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.FinancialYears.Add(new FinancialYear
        {
            Id = 8201,
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
            Id = 8202,
            FinancialYearId = 8201,
            PeriodNumber = 7,
            Name = "July",
            StartDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
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

    private sealed class JournalEntryResponse
    {
        public long Id { get; set; }
    }

    private sealed class JournalAttachmentResponse
    {
        public long Id { get; set; }
    }
}