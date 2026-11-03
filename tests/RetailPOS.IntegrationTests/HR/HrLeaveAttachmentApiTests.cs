using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Calendars;
using RetailPOS.Infrastructure.HR.Leave;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrLeaveAttachmentApiTests
{
    private const string BaseUrl = "/api/hr/v1/leave";

    [Fact]
    public async Task CleanRequiredAttachmentIsAuditedPrivateAndAllowsSubmissionPreparation()
    {
        var inspector = new AttachmentInspector(ConfidentialFileScanStatus.Clean);
        using var factory = CreateFactory(inspector);
        using var client = Client(factory, 101, 1, "hr.leave.request,hr.leave.view");
        await SeedAsync(factory.Services);

        using var created = await client.PostAsJsonAsync($"{BaseUrl}/requests", Draft());
        Assert.True(created.StatusCode == HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());
        var request = (await created.Content.ReadFromJsonAsync<HrLeaveRequestModel>())!;

        using var uploaded = await client.PostAsync(
            $"{BaseUrl}/requests/{request.Id}/attachments", Attachment());
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var metadata = await uploaded.Content.ReadAsStringAsync();
        var uploadedMetadata = (await uploaded.Content.ReadFromJsonAsync<HrLeaveRequestAttachmentModel>())!;
        Assert.Equal(ConfidentialFileScanStatus.Clean, uploadedMetadata.ScanStatus);
        Assert.DoesNotContain("StorageKey", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-key", metadata, StringComparison.OrdinalIgnoreCase);

        using var list = await client.GetAsync($"{BaseUrl}/requests/{request.Id}/attachments");
        Assert.True(list.StatusCode == HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        AssertPrivateNoStore(list);
        var listedMetadata = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("StorageKey", listedMetadata, StringComparison.OrdinalIgnoreCase);

        var attachmentId = (await list.Content.ReadFromJsonAsync<HrLeaveRequestAttachmentModel[]>())!
            .Single().Id;
        using var download = await client.GetAsync(
            $"{BaseUrl}/requests/{request.Id}/attachments/{attachmentId}");
        Assert.True(download.StatusCode == HttpStatusCode.OK,
            await download.Content.ReadAsStringAsync());
        AssertPrivateNoStore(download);
        Assert.Equal("%PDF-1.7\nleave-evidence\n", await download.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var uploadAudit = await db.AuditEvents.SingleAsync(row =>
                row.ActionType == "upload" && row.PrimaryEntityId == attachmentId.ToString());
            var metadataAudit = await db.AuditEvents.SingleAsync(row =>
                row.ActionType == "hr.leave_request.attachment_metadata_accessed");
            var downloadAudit = await db.AuditEvents.SingleAsync(row =>
                row.ActionType == "download" && row.PrimaryEntityId == attachmentId.ToString());
            Assert.Equal(1, uploadAudit.BusinessId);
            Assert.Equal(1, metadataAudit.BusinessId);
            Assert.Equal(1, downloadAudit.BusinessId);
            Assert.Equal("hr", downloadAudit.Module);
            Assert.DoesNotContain("leave-evidence", downloadAudit.ActionSummary);

            var file = await db.HrLeaveRequestAttachmentVersions.SingleAsync(row => row.Id == attachmentId);
            file.ContentType = "text/plain";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            await db.Entry(file).ReloadAsync();
            Assert.Equal("application/pdf", file.ContentType);
            db.HrLeaveRequestAttachmentVersions.Remove(file);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var row = await db.HrLeaveRequests.SingleAsync(item => item.Id == request.Id);
            var store = new HrLeaveRequestStore(db, new HrCalendarStore(db, TimeProvider.System),
                new HrLeaveStore(db), TimeProvider.System);
            var prepared = await store.PrepareSubmissionAsync(1, 101,
                new SubmitApprovalCommand(ApprovalProcessType.Leave, HrLeaveApprovalConstants.TargetType,
                    request.Id.ToString(), ApprovalScopeType.Location, row.ScopeLocationProjectionId.ToString(),
                    row.Version, row.RequestHash!), CancellationToken.None);
            Assert.NotNull(prepared);
        }
    }

    [Fact]
    public async Task LeaveYearCloseRouteBindsDateAndEnforcesPrimaryLocationAndPermission()
    {
        using var factory = CreateFactory(new AttachmentInspector(ConfidentialFileScanStatus.Clean));
        using var authorized = Client(
            factory, 101, 1, "hr.leave_balance.view,hr.leave_year.close");
        await SeedAsync(factory.Services);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var futureYearStart = new DateOnly(today.Year + 1, 1, 1);
        long entitlementVersion;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var store = new HrLeaveStore(db);
            var context = await store.ResolvePolicyAsync(1, 11, 1, futureYearStart, default);
            var payload = $"Opening|1|{futureYearStart:yyyy-MM-dd}";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(payload)));
            await store.PostLedgerEntriesAsync(1, 11, 1, context,
                [new HrLeaveLedgerWrite(HrLeaveLedgerTransactionType.Opening, 1m,
                    futureYearStart, "Manual", "future-year-opening", "future-year-opening",
                    hash, "Opening balance")], 101, default);
            entitlementVersion = (await store.GetBalancesAsync(1, 11, 1, default)).Single().Version;
        }

        using (var unauthorizedLocation = Client(
            factory, 102, 1, "hr.leave_balance.view,hr.leave_year.close"))
        using (var response = await unauthorizedLocation.PostAsJsonAsync(
            $"{BaseUrl}/employees/11/leave-years/{futureYearStart:yyyy-MM-dd}/review-close",
            new { LeaveTypeId = 1, ExpectedVersion = entitlementVersion, IdempotencyKey = "unauthorized" }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using (var missingPermission = Client(factory, 101, 1, "hr.leave_balance.view"))
        using (var response = await missingPermission.PostAsJsonAsync(
            $"{BaseUrl}/employees/11/leave-years/{futureYearStart:yyyy-MM-dd}/review-close",
            new { LeaveTypeId = 1, ExpectedVersion = entitlementVersion, IdempotencyKey = "missing-permission" }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var acceptedScope = await authorized.PostAsJsonAsync(
            $"{BaseUrl}/employees/11/leave-years/{futureYearStart:yyyy-MM-dd}/review-close",
            new { LeaveTypeId = 1, ExpectedVersion = entitlementVersion, IdempotencyKey = "future-review" });
        Assert.Equal(HttpStatusCode.Conflict, acceptedScope.StatusCode);
        Assert.Contains("before its end date", await acceptedScope.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await verifyDb.HrLeaveYearLifecycleEvents.ToListAsync());
    }

    [Theory]
    [InlineData(ConfidentialFileScanStatus.Pending, "Pending")]
    [InlineData(ConfidentialFileScanStatus.Rejected, "Rejected")]
    [InlineData(ConfidentialFileScanStatus.Error, "Error")]
    public async Task NonCleanUploadCannotBeDownloadedOrSatisfyDocumentRequirement(
        ConfidentialFileScanStatus status, string storedStatus)
    {
        var inspector = new AttachmentInspector(status);
        using var factory = CreateFactory(inspector);
        using var client = Client(factory, 101, 1, "hr.leave.request,hr.leave.view");
        await SeedAsync(factory.Services);

        using var created = await client.PostAsJsonAsync($"{BaseUrl}/requests", Draft());
        Assert.True(created.StatusCode == HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());
        var request = (await created.Content.ReadFromJsonAsync<HrLeaveRequestModel>())!;

        using var upload = await client.PostAsync(
            $"{BaseUrl}/requests/{request.Id}/attachments", Attachment());
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var file = await db.HrLeaveRequestAttachmentVersions.SingleAsync(
            row => row.BusinessId == 1 && row.LeaveRequestId == request.Id);
        Assert.Equal(storedStatus, file.ScanStatus);
        Assert.Null(file.StorageKey);
        Assert.Null((await db.HrLeaveRequests.SingleAsync(row => row.Id == request.Id))
            .CurrentAttachmentVersionId);

        using var metadata = await client.GetAsync($"{BaseUrl}/requests/{request.Id}/attachments");
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        Assert.DoesNotContain("StorageKey", await metadata.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        using var download = await client.GetAsync(
            $"{BaseUrl}/requests/{request.Id}/attachments/{file.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);

        var store = new HrLeaveRequestStore(db, new HrCalendarStore(db, TimeProvider.System),
            new HrLeaveStore(db), TimeProvider.System);
        var row = await db.HrLeaveRequests.SingleAsync(item => item.Id == request.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PrepareSubmissionAsync(
            1, 101, new SubmitApprovalCommand(ApprovalProcessType.Leave,
                HrLeaveApprovalConstants.TargetType, request.Id.ToString(), ApprovalScopeType.Location,
                row.ScopeLocationProjectionId.ToString(), row.Version, row.RequestHash!), CancellationToken.None));
    }

    [Fact]
    public async Task ActualRequestRoutesEnforceUserBusinessPermissionAndPrimaryLocationScope()
    {
        var inspector = new AttachmentInspector(ConfidentialFileScanStatus.Clean);
        using var factory = CreateFactory(inspector);
        await SeedAsync(factory.Services);

        using var owner = Client(factory, 101, 1, "hr.leave.request,hr.leave.view");
        using var created = await owner.PostAsJsonAsync($"{BaseUrl}/requests", Draft());
        Assert.True(created.StatusCode == HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());
        var request = (await created.Content.ReadFromJsonAsync<HrLeaveRequestModel>())!;
        using var uploaded = await owner.PostAsync(
            $"{BaseUrl}/requests/{request.Id}/attachments", Attachment());
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var attachment = (await uploaded.Content.ReadFromJsonAsync<HrLeaveRequestAttachmentModel>())!;

        using var outOfLocation = Client(factory, 102, 1, "hr.leave.request,hr.leave.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outOfLocation.GetAsync($"{BaseUrl}/requests/{request.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outOfLocation.GetAsync($"{BaseUrl}/requests/{request.Id}/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outOfLocation.GetAsync(
                $"{BaseUrl}/requests/{request.Id}/attachments/{attachment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outOfLocation.PostAsJsonAsync($"{BaseUrl}/requests/{request.Id}/submit",
                new { expectedVersion = request.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outOfLocation.PostAsJsonAsync($"{BaseUrl}/requests/{request.Id}/cancel",
                new { decisionKey = Guid.NewGuid().ToString("N"), reason = "test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outOfLocation.PostAsync($"{BaseUrl}/requests/{request.Id}/attachments",
                Attachment())).StatusCode);

        using var missingPermission = Client(factory, 102, 1, "");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingPermission.GetAsync($"{BaseUrl}/requests/{request.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingPermission.GetAsync($"{BaseUrl}/requests/{request.Id}/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingPermission.PostAsJsonAsync($"{BaseUrl}/requests",
                Draft())).StatusCode);

        using var otherBusiness = Client(factory, 201, 2, "hr.leave.request,hr.leave.view");
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherBusiness.GetAsync($"{BaseUrl}/requests/{request.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherBusiness.GetAsync($"{BaseUrl}/requests/{request.Id}/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherBusiness.GetAsync(
                $"{BaseUrl}/requests/{request.Id}/attachments/{attachment.Id}")).StatusCode);

        using var disabled = Client(factory, 101, 1, "hr.leave.request,hr.leave.view");
        await SetCapabilityAsync(factory.Services, "hr.enabled", false);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await disabled.GetAsync($"{BaseUrl}/requests/{request.Id}")).StatusCode);
        await SetCapabilityAsync(factory.Services, "hr.enabled", true);
        await SetCapabilityAsync(factory.Services, "attendance.enabled", false);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await disabled.GetAsync($"{BaseUrl}/requests/{request.Id}")).StatusCode);
        await SetCapabilityAsync(factory.Services, "attendance.enabled", true);

        using var cancel = await owner.PostAsJsonAsync($"{BaseUrl}/requests/{request.Id}/cancel",
            new { decisionKey = Guid.NewGuid().ToString("N"), reason = "cancel draft" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        using var get = await owner.GetAsync($"{BaseUrl}/requests/{request.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task AttachmentDownloadFailsClosedWhenAuditCannotPersist()
    {
        var inspector = new AttachmentInspector(ConfidentialFileScanStatus.Clean);
        using var factory = CreateFactory(inspector, failDownloadAudit: true);
        using var client = Client(factory, 101, 1, "hr.leave.request,hr.leave.view");
        await SeedAsync(factory.Services);

        using var created = await client.PostAsJsonAsync($"{BaseUrl}/requests", Draft());
        Assert.True(created.StatusCode == HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());
        var request = (await created.Content.ReadFromJsonAsync<HrLeaveRequestModel>())!;
        using var uploaded = await client.PostAsync(
            $"{BaseUrl}/requests/{request.Id}/attachments", Attachment());
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var attachment = (await uploaded.Content.ReadFromJsonAsync<HrLeaveRequestAttachmentModel>())!;

        using var download = await client.GetAsync(
            $"{BaseUrl}/requests/{request.Id}/attachments/{attachment.Id}");
        Assert.Equal(HttpStatusCode.InternalServerError, download.StatusCode);
        Assert.DoesNotContain("leave-evidence", await download.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
            .AuditEvents.AnyAsync(row => row.ActionType == "download"
                && row.PrimaryEntityId == attachment.Id.ToString()));
    }

    private static SqliteRoleGovernanceApiFactory CreateFactory(
        AttachmentInspector inspector, bool failDownloadAudit = false) =>
        new AttachmentApiFactory(inspector, failDownloadAudit);

    private static HttpClient Client(
        SqliteRoleGovernanceApiFactory factory, long userId, long businessId, string permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "HR");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static object Draft() => new
    {
        leaveTypeId = 1,
        startDate = DateOnly.FromDateTime(DateTime.UtcNow),
        endDate = DateOnly.FromDateTime(DateTime.UtcNow),
        reason = "Required supporting evidence"
    };

    private static MultipartFormDataContent Attachment()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.7\nleave-evidence\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "File", "leave.pdf");
        return form;
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.Select(value => value.Name),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;
        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Leave Attachments" },
            new Business { Id = 2, Name = "Other Leave Business" });
        db.Outlets.AddRange(
            new Outlet { Id = 10, BusinessId = 1, Name = "Primary Outlet", Address = "A" },
            new Outlet { Id = 20, BusinessId = 1, Name = "Additional Outlet", Address = "B" },
            new Outlet { Id = 200, BusinessId = 2, Name = "Foreign Outlet", Address = "C" });
        db.OperationalLocationProjections.AddRange(
            Location(10, 1, "10", "Primary Outlet"),
            Location(20, 1, "20", "Additional Outlet"),
            Location(200, 2, "200", "Foreign Outlet"));
        db.BusinessFeatureSettings.AddRange(
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = true },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "attendance.enabled", IsEnabled = true },
            new BusinessFeatureSetting { BusinessId = 2, FeatureKey = "hr.enabled", IsEnabled = true },
            new BusinessFeatureSetting { BusinessId = 2, FeatureKey = "attendance.enabled", IsEnabled = true });
        db.Users.AddRange(
            CreateUser(101, 1, "requester@example.invalid", 10),
            CreateUser(102, 1, "location-two@example.invalid", 20),
            CreateUser(103, 1, "no-permission@example.invalid", 10),
            CreateUser(201, 2, "foreign@example.invalid", 200));
        db.UserOutletAssignments.AddRange(
            OutletAccess(101, 1, 10), OutletAccess(102, 1, 20),
            OutletAccess(103, 1, 10), OutletAccess(201, 2, 200));
        db.HrEmploymentTypes.AddRange(
            new HrEmploymentType { Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent" },
            new HrEmploymentType { Id = 2, BusinessId = 2, Code = "PERM", Name = "Permanent" });
        db.HrEmployees.AddRange(
            Employee(11, 1, 1, 10, "EMP-11"),
            Employee(12, 1, 1, 20, "EMP-12"),
            Employee(13, 1, 1, 10, "EMP-13"),
            Employee(21, 2, 2, 200, "EMP-21"));
        db.HrEmploymentPeriods.AddRange(
            Period(11, 1, 1), Period(12, 1, 1), Period(13, 1, 1), Period(21, 2, 2));
        db.HrEmployeeLocationAssignments.AddRange(
            Assignment(11, 1, 10, today, "Primary"),
            Assignment(11, 1, 20, today, "Additional"),
            Assignment(12, 1, 20, today, "Primary"),
            Assignment(13, 1, 10, today, "Primary"),
            Assignment(21, 2, 200, today, "Primary"));
        db.HrEmployeeUserLinks.AddRange(
            Link(11, 1, 101, now), Link(12, 1, 102, now),
            Link(13, 1, 103, now), Link(21, 2, 201, now));
        db.Set<HrShift>().Add(new HrShift
        {
            Id = 1, BusinessId = 1, Code = "DAY", Name = "Day",
            StartTimeLocal = new TimeOnly(9, 0), EndTimeLocal = new TimeOnly(17, 0),
            MinimumWorkMinutes = 420, IsActive = true
        });
        db.Set<HrRosterAssignment>().Add(new HrRosterAssignment
        {
            Id = 1, BusinessId = 1, EmployeeId = 11, WorkDate = today, ShiftId = 1,
            OperationalLocationProjectionId = 10, Status = "Published", DayClassification = "NormalWorkingDay",
            PublishedByUserId = 101, PublishedAtUtc = now,
            ShiftSnapshotJson = """{"code":"DAY","start":"09:00","end":"17:00"}""",
            LocationSnapshotJson = """{"name":"Primary Outlet"}""",
            CalendarEvidenceJson = JsonSerializer.Serialize(new HrDayClassificationResponse(
                today, "NormalWorkingDay", "PublishedRoster", false, false, [], null))
        });
        await db.SaveChangesAsync();

        var leaveStore = new HrLeaveStore(db);
        await leaveStore.CreateTypeAsync(1, new HrLeaveTypeInput("MEDICAL", "Medical", true, true),
            CancellationToken.None);
        await leaveStore.CreatePolicyVersionAsync(1, new HrLeavePolicyInput(
            1, "STANDARD", "Standard", HrLeavePolicyScopeType.Business, null, 1,
            HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart, 12m, false, null, null,
            null, true, null, 1, 1, new DateOnly(today.Year, 1, 1), null, true),
            CancellationToken.None);
    }

    private static OperationalLocationProjection Location(long id, long businessId, string sourceId, string name) =>
        new()
        {
            Id = id, BusinessId = businessId, SourceSystem = "RetailPOS",
            SourceEntityType = "Outlet", SourceEntityId = sourceId, Name = name, SourceVersion = 1
        };

    private static User CreateUser(long id, long businessId, string email, long outletId) =>
        new()
        {
            Id = id, BusinessId = businessId, Name = $"User {id}", Email = email,
            PasswordHash = "test", IsActive = true, OutletId = outletId,
            InventoryLocationAccessScope = User.InventoryAccessSpecific
        };

    private static UserOutletAssignment OutletAccess(long userId, long businessId, long outletId) =>
        new() { UserId = userId, BusinessId = businessId, OutletId = outletId, IsActive = true };

    private static HrEmployee Employee(long id, long businessId, long employmentTypeId,
        long locationId, string code) =>
        new()
        {
            Id = id, BusinessId = businessId, EmployeeCode = code, FirstName = "Test",
            DisplayName = code, CurrentEmploymentTypeId = employmentTypeId,
            CurrentEmploymentStatus = "Active", JoiningDate = new DateOnly(2020, 1, 1),
            CurrentPrimaryLocationProjectionId = locationId
        };

    private static HrEmploymentPeriod Period(long employeeId, long businessId, long employmentTypeId) =>
        new()
        {
            BusinessId = businessId, EmployeeId = employeeId, EmploymentTypeId = employmentTypeId,
            StartDate = new DateOnly(2020, 1, 1), Status = "Active"
        };

    private static HrEmployeeLocationAssignment Assignment(
        long employeeId, long businessId, long locationId, DateOnly effectiveFrom, string type) =>
        new()
        {
            BusinessId = businessId, EmployeeId = employeeId, OperationalLocationProjectionId = locationId,
            LocationNameSnapshot = $"Location {locationId}", SourceSystemSnapshot = "RetailPOS",
            SourceEntityTypeSnapshot = "Outlet", SourceEntityIdSnapshot = locationId.ToString(),
            LocationSourceVersionSnapshot = 1, AssignmentType = type, EffectiveFrom = effectiveFrom,
            Status = "Effective", RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow
        };

    private static HrEmployeeUserLink Link(long employeeId, long businessId, long userId, DateTime now) =>
        new()
        {
            BusinessId = businessId, EmployeeId = employeeId, IdentityUserId = userId,
            LinkedAtUtc = now, LinkedByUserId = userId, LinkReason = "Acceptance test", IsActive = true
        };

    private static async Task SetCapabilityAsync(IServiceProvider services, string featureKey, bool enabled)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var setting = await db.BusinessFeatureSettings.SingleAsync(row =>
            row.BusinessId == 1 && row.FeatureKey == featureKey);
        setting.IsEnabled = enabled;
        await db.SaveChangesAsync();
    }

    private sealed class AttachmentInspector(ConfidentialFileScanStatus scanStatus)
        : IConfidentialContentInspector
    {
        public Task<ConfidentialContentValidation> ValidateAsync(
            ReadOnlyMemory<byte> content, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(new ConfidentialContentValidation("application/pdf", ".pdf"));

        public Task<ConfidentialFileScanStatus> ScanAsync(
            ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
            Task.FromResult(scanStatus);
    }

    private sealed class AttachmentApiFactory(
        AttachmentInspector inspector, bool failDownloadAudit) : SqliteRoleGovernanceApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IPrivateBlobStore>(new ImportTestBlobs());
                services.AddSingleton<IConfidentialFileEncryption, ImportTestEncryption>();
                services.AddSingleton<IConfidentialContentInspector>(inspector);
                services.AddSingleton(new ConfidentialFileLimits(100_000, 100_000));
                services.AddScoped<ConfidentialFileService>();
                if (failDownloadAudit)
                    services.ConfigureDbContext<RetailPOSDbContext>(options =>
                        options.AddInterceptors(new FailLeaveDownloadAuditInterceptor()));
            });
        }
    }

    private sealed class FailLeaveDownloadAuditInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.ActionType == "download"))
                throw new IOException("Injected leave attachment access-audit failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
