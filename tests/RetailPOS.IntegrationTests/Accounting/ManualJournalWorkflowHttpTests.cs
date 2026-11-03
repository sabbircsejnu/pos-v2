using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class ManualJournalWorkflowHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ManualJournalWorkflowHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ModeB_Create_Submit_Approve_Post_Lifecycle_Works()
    {
        await ResetAndSeedAsync(ManualJournalWorkflowModes.ApprovalRequired, allowSelfApproval: false);
        using var maker = CreateClient(101, "AccountsAdmin", "accounting.manual_journals.create,accounting.manual_journals.submit,accounting.manual_journals.view");
        using var checker = CreateClient(102, "AccountsAdmin", "accounting.manual_journals.approve,accounting.manual_journals.post,accounting.manual_journals.reverse,accounting.manual_journals.view");

        var createResponse = await maker.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
            description = "Mode B manual journal",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 120m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 120m }
            }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);
        Assert.Equal(JournalEntryStatuses.Draft, created!.Status);
        Assert.Equal(1, created.BusinessId);
        Assert.Equal(101, created.CreatedBy);
        Assert.Equal("Mode B manual journal", created.Description);
        Assert.Equal(new DateTime(2026, 7, 21), created.BusinessDate);
        Assert.False(created.IsSystemGenerated);
        Assert.Equal(2, created.Lines.Count);

        var submitResponse = await maker.PostAsync($"/api/accounting/journals/{created.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = await ReadData<JournalEntryResponse>(submitResponse);
        Assert.NotNull(submitted);
        Assert.Equal(JournalEntryStatuses.Submitted, submitted!.Status);

        var approveResponse = await checker.PostAsJsonAsync($"/api/accounting/journals/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = await ReadData<JournalEntryResponse>(approveResponse);
        Assert.NotNull(approved);
        Assert.Equal(JournalEntryStatuses.Approved, approved!.Status);
        Assert.Equal(102, approved.ApprovedBy);
        var duplicateApproveResponse = await checker.PostAsJsonAsync(
            $"/api/accounting/journals/{created.Id}/approve",
            new { });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateApproveResponse.StatusCode);

        var postResponse = await checker.PostAsync($"/api/accounting/journals/{created.Id}/post", null);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var posted = await ReadData<JournalEntryResponse>(postResponse);
        Assert.NotNull(posted);
        Assert.Equal(JournalEntryStatuses.Posted, posted!.Status);
        Assert.Equal(102, posted.PostedBy);
        Assert.False(string.IsNullOrWhiteSpace(posted.JournalNumber));

        var duplicatePostResponse = await checker.PostAsync(
            $"/api/accounting/journals/{created.Id}/post",
            null);
        Assert.Equal(HttpStatusCode.OK, duplicatePostResponse.StatusCode);
        var duplicatePosted = await ReadData<JournalEntryResponse>(duplicatePostResponse);
        Assert.NotNull(duplicatePosted);
        Assert.Equal(posted.JournalNumber, duplicatePosted!.JournalNumber);
        Assert.Equal(posted.PostedBy, duplicatePosted.PostedBy);

        var postingDate = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);
        var reverseResponse = await checker.PostAsJsonAsync(
            $"/api/accounting/journals/{created.Id}/reverse",
            new
            {
                reason = "HTTP workflow correction",
                postingDate,
                businessDate = postingDate
            });
        Assert.Equal(HttpStatusCode.OK, reverseResponse.StatusCode);
        var reversal = await ReadData<JournalEntryResponse>(reverseResponse);
        Assert.NotNull(reversal);
        Assert.Equal(JournalEntryStatuses.Posted, reversal!.Status);
        Assert.Equal("JournalReversal", reversal.JournalType);
        Assert.Equal(created.Id, reversal.ReversalOfJournalEntryId);

        var originalResponse = await checker.GetAsync($"/api/accounting/journals/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
        var reversedOriginal = await ReadData<JournalEntryResponse>(originalResponse);
        Assert.NotNull(reversedOriginal);
        Assert.Equal(JournalEntryStatuses.Reversed, reversedOriginal!.Status);
        Assert.Equal(reversal.Id, reversedOriginal.ReversedByJournalEntryId);
    }

    [Fact]
    public async Task ModeB_SelfApproval_IsRejected_WhenDisabled()
    {
        await ResetAndSeedAsync(ManualJournalWorkflowModes.ApprovalRequired, allowSelfApproval: false);
        using var maker = CreateClient(101, "AccountsAdmin", "accounting.manual_journals.create,accounting.manual_journals.submit,accounting.manual_journals.approve,accounting.manual_journals.view");

        var createResponse = await maker.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
            description = "Self approval blocked",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 10m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 10m }
            }
        });
        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);

        var submitResponse = await maker.PostAsync($"/api/accounting/journals/{created!.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var approveResponse = await maker.PostAsJsonAsync($"/api/accounting/journals/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.BadRequest, approveResponse.StatusCode);
    }

    [Fact]
    public async Task BusinessOwnerSelfApproval_IsAllowed_WhenEnabled()
    {
        await ResetAndSeedAsync(ManualJournalWorkflowModes.ApprovalRequired, allowSelfApproval: true);
        using var owner = CreateClient(101, "BusinessOwner", "accounting.manual_journals.create,accounting.manual_journals.submit,accounting.manual_journals.approve,accounting.manual_journals.view");

        var createResponse = await owner.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc),
            description = "Owner self approval",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 25m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 25m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);

        var submitResponse = await owner.PostAsync($"/api/accounting/journals/{created!.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var approveResponse = await owner.PostAsJsonAsync(
            $"/api/accounting/journals/{created.Id}/approve",
            new { });

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = await ReadData<JournalEntryResponse>(approveResponse);
        Assert.NotNull(approved);
        Assert.Equal(JournalEntryStatuses.Approved, approved!.Status);
        Assert.Equal(101, approved.ApprovedBy);
    }

    [Fact]
    public async Task Reject_RequiresSubmittedManualJournalAndRecordsReasonAndActor()
    {
        await ResetAndSeedAsync(ManualJournalWorkflowModes.ApprovalRequired, allowSelfApproval: false);
        using var maker = CreateClient(101, "AccountsAdmin", "accounting.manual_journals.create,accounting.manual_journals.submit,accounting.manual_journals.view");
        using var checker = CreateClient(102, "AccountsAdmin", "accounting.manual_journals.reject,accounting.manual_journals.view");

        var createResponse = await maker.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
            description = "Journal to reject",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 32m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 32m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);
        Assert.Equal(HttpStatusCode.OK, (await maker.PostAsync(
            $"/api/accounting/journals/{created!.Id}/submit",
            null)).StatusCode);

        var rejectResponse = await checker.PostAsJsonAsync(
            $"/api/accounting/journals/{created.Id}/reject",
            new { reason = "  Supporting evidence required  " });

        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var rejected = await ReadData<JournalEntryResponse>(rejectResponse);
        Assert.NotNull(rejected);
        Assert.Equal(JournalEntryStatuses.Rejected, rejected!.Status);
        Assert.Equal("Supporting evidence required", rejected.RejectionReason);
        Assert.Equal(102, rejected.RejectedBy);
    }

    [Fact]
    public async Task DraftUpdate_ReplacesLinesAndPreservesUpdatedActor()
    {
        await ResetAndSeedAsync(ManualJournalWorkflowModes.DraftPost, allowSelfApproval: false);
        using var client = CreateClient(101, "AccountsAdmin", "accounting.manual_journals.create,accounting.manual_journals.update,accounting.manual_journals.view");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc),
            description = "Before update",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 40m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 40m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync($"/api/accounting/journals/{created!.Id}", new
        {
            journalType = "ManualJournal",
            transactionDate = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc),
            businessDate = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc),
            referenceType = "Manual",
            referenceId = "UPDATE-1",
            referenceNumber = "R-001",
            description = "After update",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 55m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 55m }
            }
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadData<JournalEntryResponse>(updateResponse);
        Assert.NotNull(updated);
        Assert.Equal("After update", updated!.Description);
        Assert.Equal("R-001", updated.ReferenceNumber);
        Assert.Equal(101, updated.UpdatedBy);
        Assert.Equal(2, updated.Lines.Count);
        Assert.Contains(updated.Lines, line => line.DebitAmount == 55m);
        Assert.Contains(updated.Lines, line => line.CreditAmount == 55m);
    }

    [Fact]
    public async Task Attachment_Upload_List_Download_Delete_Works()
    {
        await ResetAndSeedAsync(ManualJournalWorkflowModes.DraftPost, allowSelfApproval: false);
        using var client = CreateClient(101, "AccountsAdmin", "accounting.manual_journals.create,accounting.manual_journals.update,accounting.manual_journals.view");

        var createResponse = await client.PostAsJsonAsync("/api/accounting/journals", new
        {
            transactionDate = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc),
            postingDate = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc),
            description = "Attachment journal",
            lines = new object[]
            {
                new { accountId = 1001L, debitAmount = 15m, creditAmount = 0m },
                new { accountId = 2001L, debitAmount = 0m, creditAmount = 15m }
            }
        });
        var created = await ReadData<JournalEntryResponse>(createResponse);
        Assert.NotNull(created);

        using (var emptyForm = new MultipartFormDataContent())
        using (var emptyContent = new ByteArrayContent(Array.Empty<byte>()))
        {
            emptyForm.Add(emptyContent, "File", "empty.pdf");
            var emptyResponse = await client.PostAsync(
                $"/api/accounting/journals/{created!.Id}/attachments",
                emptyForm);
            Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);
            var emptyError = await emptyResponse.Content.ReadFromJsonAsync<ApiEnvelope<object>>();
            Assert.Equal("Attachment file is required.", emptyError!.Message);
        }

        using (var unsupportedForm = new MultipartFormDataContent())
        using (var unsupportedContent = new ByteArrayContent(Encoding.UTF8.GetBytes("unsupported")))
        {
            unsupportedForm.Add(unsupportedContent, "File", "evidence.txt");
            var unsupportedResponse = await client.PostAsync(
                $"/api/accounting/journals/{created!.Id}/attachments",
                unsupportedForm);
            Assert.Equal(HttpStatusCode.BadRequest, unsupportedResponse.StatusCode);
            var unsupportedError = await unsupportedResponse.Content.ReadFromJsonAsync<ApiEnvelope<object>>();
            Assert.Equal("Unsupported attachment file type.", unsupportedError!.Message);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var configuration = await db.AccountingConfigurations.SingleAsync(item => item.BusinessId == 1);
            configuration.ManualJournalAttachmentMaxBytes = 3;
            await db.SaveChangesAsync();
        }

        using (var oversizedForm = new MultipartFormDataContent())
        using (var oversizedContent = new ByteArrayContent(Encoding.UTF8.GetBytes("oversized")))
        {
            oversizedForm.Add(oversizedContent, "File", "evidence.pdf");
            var oversizedResponse = await client.PostAsync(
                $"/api/accounting/journals/{created!.Id}/attachments",
                oversizedForm);
            Assert.Equal(HttpStatusCode.BadRequest, oversizedResponse.StatusCode);
            var oversizedError = await oversizedResponse.Content.ReadFromJsonAsync<ApiEnvelope<object>>();
            Assert.Equal(
                "Attachment exceeds the configured maximum size of 3 bytes.",
                oversizedError!.Message);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var configuration = await db.AccountingConfigurations.SingleAsync(item => item.BusinessId == 1);
            configuration.ManualJournalAttachmentMaxBytes = 10 * 1024 * 1024;
            await db.SaveChangesAsync();
        }

        using var form = new MultipartFormDataContent();
        var attachmentContent = Encoding.UTF8.GetBytes("sample journal attachment");
        using var fileContent = new ByteArrayContent(attachmentContent);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "File", "evidence.pdf");

        var uploadResponse = await client.PostAsync($"/api/accounting/journals/{created!.Id}/attachments", form);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var uploaded = await ReadData<JournalAttachmentResponse>(uploadResponse);
        Assert.NotNull(uploaded);
        Assert.Equal("evidence.pdf", uploaded!.FileName);
        Assert.Equal("application/pdf", uploaded.ContentType);
        Assert.Equal(attachmentContent.Length, uploaded.FileSizeBytes);

        var listResponse = await client.GetAsync($"/api/accounting/journals/{created.Id}/attachments");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = await ReadData<List<JournalAttachmentResponse>>(listResponse);
        Assert.NotNull(listed);
        Assert.Collection(
            listed!,
            attachment =>
            {
                Assert.Equal(uploaded.Id, attachment.Id);
                Assert.Equal("evidence.pdf", attachment.FileName);
            });

        var downloadResponse = await client.GetAsync($"/api/accounting/journals/{created.Id}/attachments/{uploaded!.Id}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal(attachmentContent, await downloadResponse.Content.ReadAsByteArrayAsync());
        Assert.Contains("evidence.pdf", downloadResponse.Content.Headers.ContentDisposition!.FileName);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var journal = await db.JournalEntries.SingleAsync(item => item.Id == created.Id);
            journal.Status = JournalEntryStatuses.Posted;
            await db.SaveChangesAsync();
        }

        using (var postedUploadForm = new MultipartFormDataContent())
        using (var postedUploadContent = new ByteArrayContent(Encoding.UTF8.GetBytes("posted")))
        {
            postedUploadForm.Add(postedUploadContent, "File", "posted.pdf");
            var postedUploadResponse = await client.PostAsync(
                $"/api/accounting/journals/{created.Id}/attachments",
                postedUploadForm);
            Assert.Equal(HttpStatusCode.BadRequest, postedUploadResponse.StatusCode);
            var postedUploadError = await postedUploadResponse.Content.ReadFromJsonAsync<ApiEnvelope<object>>();
            Assert.Equal(
                "Attachments can only be modified while the journal is in a pre-posted editable state.",
                postedUploadError!.Message);
        }

        var postedDeleteResponse = await client.DeleteAsync(
            $"/api/accounting/journals/{created.Id}/attachments/{uploaded.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, postedDeleteResponse.StatusCode);
        var postedDeleteError = await postedDeleteResponse.Content.ReadFromJsonAsync<ApiEnvelope<object>>();
        Assert.Equal(
            "Attachments can only be deleted while the journal is in a pre-posted editable state.",
            postedDeleteError!.Message);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var journal = await db.JournalEntries.SingleAsync(item => item.Id == created.Id);
            journal.Status = JournalEntryStatuses.Draft;
            await db.SaveChangesAsync();
        }

        var deleteResponse = await client.DeleteAsync($"/api/accounting/journals/{created.Id}/attachments/{uploaded.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var emptyListResponse = await client.GetAsync($"/api/accounting/journals/{created.Id}/attachments");
        Assert.Empty((await ReadData<List<JournalAttachmentResponse>>(emptyListResponse))!);
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

    private async Task ResetAndSeedAsync(string workflowMode, bool allowSelfApproval)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Manual HTTP Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        db.Users.AddRange(
            new User { Id = 101, BusinessId = 1, Name = "Maker", Email = "maker@example.test", PasswordHash = "test", OutletId = 11, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Id = 102, BusinessId = 1, Name = "Checker", Email = "checker@example.test", PasswordHash = "test", OutletId = 11, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

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
            ManualJournalWorkflowMode = workflowMode,
            ManualJournalAllowSelfApproval = allowSelfApproval,
            ManualJournalAttachmentMaxBytes = 10 * 1024 * 1024,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.FinancialYears.Add(new FinancialYear
        {
            Id = 8101,
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
            Id = 8102,
            FinancialYearId = 8101,
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

    private static async Task<T?> ReadData<T>(HttpResponseMessage response) where T : class
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions);
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

    private sealed class JournalEntryResponse
    {
        public long Id { get; set; }
        public long BusinessId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? JournalNumber { get; set; }
        public string? Description { get; set; }
        public DateTime? BusinessDate { get; set; }
        public long? CreatedBy { get; set; }
        public long? UpdatedBy { get; set; }
        public long? ApprovedBy { get; set; }
        public long? PostedBy { get; set; }
        public string? JournalType { get; set; }
        public long? ReversalOfJournalEntryId { get; set; }
        public long? ReversedByJournalEntryId { get; set; }
        public long? RejectedBy { get; set; }
        public string? RejectionReason { get; set; }
        public bool IsSystemGenerated { get; set; }
        public string? ReferenceNumber { get; set; }
        public List<JournalEntryLineResponse> Lines { get; set; } = new();
    }

    private sealed class JournalEntryLineResponse
    {
        public long AccountId { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
    }

    private sealed class JournalAttachmentResponse
    {
        public long Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
    }
}