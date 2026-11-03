using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RetailPOS.API.Controllers.HR;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Application.HR.Reports;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Overtime;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrRosterApiTests
{
    [Fact]
    public async Task DraftPublishClassificationAndStaleApiContract()
    {
        using var factory = new SqliteRoleGovernanceApiFactory(); using var c = factory.CreateClient();
        await Seed(factory.Services); Auth(c, "hr.roster.manage,hr.roster.view,hr.shift.manage");
        var shift = await c.PostAsJsonAsync("/api/hr/v1/shifts", HrCalendarPersistenceTests.Shift());
        var s = (await shift.Content.ReadFromJsonAsync<HrShiftResponse>())!;
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var creation = await c.PostAsJsonAsync("/api/hr/v1/rosters", new HrRosterData(1, date, s.Id, 10));
        Assert.True(creation.StatusCode == HttpStatusCode.Created, await creation.Content.ReadAsStringAsync());
        Assert.NotNull(creation.Headers.Location);
        var row = (await creation.Content.ReadFromJsonAsync<HrRosterResponse>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/hr/v1/rosters/classification?employeeId=1&workDate={date:yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/hr/v1/rosters/{row.Id}/publish", new { expectedVersion = row.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/hr/v1/rosters/{row.Id}/publish", new { expectedVersion = row.Version })).StatusCode);
        var result = await c.GetFromJsonAsync<HrDayClassificationResponse>($"/api/hr/v1/rosters/classification?employeeId=1&workDate={date:yyyy-MM-dd}");
        Assert.Equal("PublishedRoster", result!.Source);
        Assert.Single((await c.GetFromJsonAsync<HrRosterResponse[]>($"/api/hr/v1/rosters?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"))!);
    }
    [Theory]
    [InlineData(false, true)] [InlineData(true, false)]
    public async Task BothCapabilitiesRequired(bool hr, bool attendance)
    {
        using var factory = new SqliteRoleGovernanceApiFactory(); using var c = factory.CreateClient();
        await Seed(factory.Services, hr, attendance); Auth(c, "hr.roster.view,hr.roster.manage");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/hr/v1/rosters?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}")).StatusCode);
    }
    [Theory]
    [InlineData("", true)] [InlineData("hr.roster.manage", false)]
    public async Task PermissionAndIdentityLocationScopeNotHrAssignmentRequired(string permissions, bool global)
    {
        using var factory = new SqliteRoleGovernanceApiFactory(); using var c = factory.CreateClient();
        await Seed(factory.Services, global: global); Auth(c, permissions);
        var response = await c.PostAsJsonAsync("/api/hr/v1/rosters", new HrRosterData(1, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), 1, 10));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Fact]
    public async Task ForeignEmployeeNeverCreatesRoster()
    {
        using var factory = new SqliteRoleGovernanceApiFactory(); using var c = factory.CreateClient();
        await Seed(factory.Services); Auth(c, "hr.roster.manage");
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/hr/v1/rosters",
            new HrRosterData(999, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), 1, 10))).StatusCode);
    }
    [Fact]
    public async Task DailyAttendanceAndProcessingEndpointsRequireAttendancePermissions()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory.Services);
        Auth(client, "hr.attendance.view,hr.attendance.reprocess");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var listed = await client.GetAsync($"/api/hr/v1/attendance/daily?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        var processed = await client.PostAsJsonAsync("/api/hr/v1/attendance/process",
            new { From = today, To = today });

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Empty((await listed.Content.ReadFromJsonAsync<HrDailyAttendanceResponse[]>())!);
        Assert.Equal(HttpStatusCode.OK, processed.StatusCode);
        Assert.Empty((await processed.Content.ReadFromJsonAsync<HrDailyAttendanceResponse[]>())!);

        using var denied = factory.CreateClient();
        Auth(denied, "hr.roster.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await denied.GetAsync($"/api/hr/v1/attendance/daily?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsJsonAsync("/api/hr/v1/attendance/process",
            new { From = today, To = today })).StatusCode);
    }

    [Fact]
    public async Task AttendanceReportRequiresItsPermissionAndAttendanceCapabilityAndRejectsForeignLocation()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var authorized = factory.CreateClient();
        await Seed(factory.Services);
        Auth(authorized, "hr.attendance.report");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await authorized.GetAsync(
            $"/api/hr/v1/attendance/reports?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Equal(0, (await report.Content.ReadFromJsonAsync<HrAttendanceReportPage>())!.TotalCount);

        using var wrongPermission = factory.CreateClient();
        Auth(wrongPermission, "hr.attendance.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongPermission.GetAsync(
            $"/api/hr/v1/attendance/reports?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await authorized.GetAsync(
            $"/api/hr/v1/attendance/reports?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&locationProjectionId=20"))
            .StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await authorized.GetAsync(
            $"/api/hr/v1/attendance/reports?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&status=Unknown"))
            .StatusCode);

        using var disabledFactory = new SqliteRoleGovernanceApiFactory();
        using var disabled = disabledFactory.CreateClient();
        await Seed(disabledFactory.Services, attendance: false);
        Auth(disabled, "hr.attendance.report");
        Assert.Equal(HttpStatusCode.Forbidden, (await disabled.GetAsync(
            $"/api/hr/v1/attendance/reports?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task OvertimeHttpRoutesEnforcePermissionAndBothCapabilities()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var authorized = factory.CreateClient();
        Auth(authorized, "hr.overtime.view,hr.overtime.calculate");
        var policies = await authorized.GetAsync("/api/hr/v1/overtime/policies");
        Assert.Equal(HttpStatusCode.OK, policies.StatusCode);
        Assert.Empty((await policies.Content.ReadFromJsonAsync<object[]>())!);
        var entries = await authorized.GetAsync(
            $"/api/hr/v1/overtime/entries?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, entries.StatusCode);
        Assert.Empty((await entries.Content.ReadFromJsonAsync<object[]>())!);
        Assert.Equal(HttpStatusCode.OK, (await authorized.PostAsJsonAsync("/api/hr/v1/overtime/calculate",
            new { From = today, To = today })).StatusCode);

        using var wrongPermission = factory.CreateClient();
        Auth(wrongPermission, "hr.attendance.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await wrongPermission.GetAsync("/api/hr/v1/overtime/policies")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await wrongPermission.PostAsJsonAsync("/api/hr/v1/overtime/calculate",
                new { From = today, To = today })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await wrongPermission.GetAsync("/api/hr/v1/overtime/entries/999")).StatusCode);

        using var disabledFactory = new SqliteRoleGovernanceApiFactory();
        using var disabled = disabledFactory.CreateClient();
        await Seed(disabledFactory.Services, attendance: false);
        Auth(disabled, "hr.overtime.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await disabled.GetAsync("/api/hr/v1/overtime/policies")).StatusCode);
    }

    [Fact]
    public async Task ExitHttpWorkflowRequiresHrPermissionAndScopesBusinessBoundaries()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        using var authorized = factory.CreateClient();
        Auth(authorized, "hr.exit.view,hr.exit.initiate,hr.exit.clearance");

        Assert.Equal(HttpStatusCode.OK,
            (await authorized.GetAsync("/api/hr/v1/exits/templates")).StatusCode);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var templateResponse = await authorized.PostAsJsonAsync("/api/hr/v1/exits/templates", new
        {
            code = "EXIT-DEFAULT",
            name = "Default checklist",
            scopeType = 0,
            scopeReference = (string?)null,
            versionNo = 1,
            effectiveFrom = today,
            effectiveTo = (DateOnly?)null,
            isActive = true,
            items = new[]
            {
                new { sequence = 1, code = "ASSET", name = "Return assets",
                    responsibleRole = "Operations", responsibleReference = (string?)null, isMandatory = true }
            }
        });
        Assert.Equal(HttpStatusCode.Created, templateResponse.StatusCode);

        var create = await authorized.PostAsJsonAsync("/api/hr/v1/exits", new
        {
            employeeId = 1, exitType = "Resignation", initiatedDate = today,
            proposedLastWorkingDate = today, reason = "Notice submitted."
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var exit = (await create.Content.ReadFromJsonAsync<HrExitCaseModel>())!;
        Assert.Equal(HttpStatusCode.OK, (await authorized.GetAsync($"/api/hr/v1/exits/{exit.Id}")).StatusCode);
        Assert.Single((await authorized.GetFromJsonAsync<HrExitCaseModel[]>("/api/hr/v1/exits"))!);

        using var denied = factory.CreateClient();
        Auth(denied, "hr.roster.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync("/api/hr/v1/exits")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await denied.PostAsJsonAsync("/api/hr/v1/exits", new
            {
                employeeId = 1, exitType = "Resignation", initiatedDate = today,
                proposedLastWorkingDate = today, reason = "No permission."
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await authorized.GetAsync("/api/hr/v1/exits/999999")).StatusCode);

        var cancelled = await authorized.PostAsJsonAsync($"/api/hr/v1/exits/{exit.Id}/cancel",
            new { expectedVersion = exit.Version, decisionKey = Guid.NewGuid().ToString(), reason = "Withdrawn." });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var second = await authorized.PostAsJsonAsync("/api/hr/v1/exits", new
        {
            employeeId = 1, exitType = "Retirement", initiatedDate = today,
            proposedLastWorkingDate = today, reason = "Retirement notice."
        });
        var secondCase = (await second.Content.ReadFromJsonAsync<HrExitCaseModel>())!;
        var submitted = await authorized.PostAsJsonAsync($"/api/hr/v1/exits/{secondCase.Id}/submit",
            new { expectedVersion = secondCase.Version });
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        var approved = await authorized.GetFromJsonAsync<HrExitCaseModel>($"/api/hr/v1/exits/{secondCase.Id}");
        Assert.Equal("Approved", approved!.Status);
        var mandatory = Assert.Single(approved.ClearanceItems);
        var cleared = await authorized.PutAsJsonAsync("/api/hr/v1/exits/clearance-items", new
        {
            itemId = mandatory.Id, expectedVersion = mandatory.Version, status = "Cleared",
            comment = (string?)null, evidenceReference = "asset-receipt-1"
        });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var ready = (await cleared.Content.ReadFromJsonAsync<HrExitCaseModel>())!;
        var completed = await authorized.PostAsJsonAsync($"/api/hr/v1/exits/{ready.Id}/complete",
            new { expectedVersion = ready.Version, actualLastWorkingDate = today, reason = "Checklist complete." });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        using var unauthenticated = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await unauthenticated.GetAsync("/api/hr/v1/exits")).StatusCode);
    }

    [Fact]
    public async Task ExitHttpEnforcesPrimaryLocationAndHrCapabilityWithoutCrossBusinessDisclosure()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services, global: false);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet { Id = 20, BusinessId = 2, Name = "Foreign outlet" });
            db.UserOutletAssignments.Add(new UserOutletAssignment
            {
                UserId = 101, BusinessId = 1, OutletId = 10, IsPrimary = true
            });
            db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Unassigned outlet" });
            db.OperationalLocationProjections.Add(new OperationalLocationProjection
            {
                Id = 11, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
                SourceEntityId = "11", Name = "Unassigned outlet", SourceVersion = 1
            });
            db.HrEmployees.Add(new HrEmployee
            {
                Id = 2, BusinessId = 1, EmployeeCode = "EMP-2", FirstName = "Additional",
                DisplayName = "Additional assignment employee", CurrentEmploymentTypeId = 1,
                CurrentPrimaryLocationProjectionId = 11, JoiningDate = new DateOnly(2026, 1, 1)
            });
            db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
            {
                BusinessId = 1, EmployeeId = 2, EmploymentTypeId = 1,
                StartDate = new DateOnly(2026, 1, 1), Status = "Active"
            });
            db.HrEmployeeLocationAssignments.AddRange(
                new HrEmployeeLocationAssignment
                {
                    BusinessId = 1, EmployeeId = 2, OperationalLocationProjectionId = 11,
                    AssignmentType = "Primary", LocationNameSnapshot = "Unassigned outlet",
                    SourceSystemSnapshot = "RetailPOS", SourceEntityTypeSnapshot = "Outlet",
                    SourceEntityIdSnapshot = "11", LocationSourceVersionSnapshot = 1,
                    EffectiveFrom = new DateOnly(2026, 1, 1), RequestedByUserId = 101,
                    RequestedAtUtc = DateTime.UtcNow
                },
                new HrEmployeeLocationAssignment
                {
                    BusinessId = 1, EmployeeId = 2, OperationalLocationProjectionId = 10,
                    AssignmentType = "Additional", LocationNameSnapshot = "Outlet",
                    SourceSystemSnapshot = "RetailPOS", SourceEntityTypeSnapshot = "Outlet",
                    SourceEntityIdSnapshot = "10", LocationSourceVersionSnapshot = 1,
                    EffectiveFrom = new DateOnly(2026, 1, 1), RequestedByUserId = 101,
                    RequestedAtUtc = DateTime.UtcNow
                });
            var foreignTemplate = new HrClearanceTemplate
            {
                BusinessId = 2, Code = "FOREIGN", Name = "Foreign checklist",
                ScopeType = HrClearanceTemplateScopeType.Business, VersionNo = 1,
                EffectiveFrom = today, IsActive = true, CreatedByUserId = 201,
                Items = [new HrClearanceTemplateItem
                {
                    BusinessId = 2, Sequence = 1, Code = "ITEM", Name = "Foreign item", IsMandatory = true
                }]
            };
            db.HrEmploymentTypes.Add(new HrEmploymentType
            {
                Id = 2, BusinessId = 2, Code = "PERM", Name = "Permanent"
            });
            var foreignEmployee = new HrEmployee
            {
                Id = 20, BusinessId = 2, EmployeeCode = "FOREIGN-EMP", FirstName = "Foreign",
                DisplayName = "Foreign employee", CurrentEmploymentTypeId = 2,
                CurrentPrimaryLocationProjectionId = 20, JoiningDate = new DateOnly(2026, 1, 1)
            };
            db.HrEmployees.Add(foreignEmployee);
            db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
            {
                BusinessId = 2, EmployeeId = 20, EmploymentTypeId = 2,
                StartDate = new DateOnly(2026, 1, 1), Status = "Active"
            });
            db.HrClearanceTemplates.Add(foreignTemplate);
            await db.SaveChangesAsync();
            db.HrExitCases.Add(new HrExitCase
            {
                BusinessId = 2, EmployeeId = 20, ExitType = "Resignation", InitiatedDate = today,
                ProposedLastWorkingDate = today, Reason = "Foreign case", Status = "Draft",
                InitiatedByUserId = 201, InitiatedAtUtc = DateTime.UtcNow,
                PrimaryLocationProjectionIdSnapshot = 20, ClearanceTemplateId = foreignTemplate.Id,
                ClearanceTemplateVersionNo = 1, ClearanceTemplateSnapshotJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        Auth(client, "hr.exit.view,hr.exit.initiate,hr.exit.clearance");
        var template = await client.PostAsJsonAsync("/api/hr/v1/exits/templates", new
        {
            code = "EXIT-DEFAULT",
            name = "Default checklist",
            scopeType = 0,
            scopeReference = (string?)null,
            versionNo = 1,
            effectiveFrom = today,
            effectiveTo = (DateOnly?)null,
            isActive = true,
            items = new[]
            {
                new { sequence = 1, code = "ASSET", name = "Return assets",
                    responsibleRole = "Operations", responsibleReference = (string?)null, isMandatory = true }
            }
        });
        Assert.Equal(HttpStatusCode.Created, template.StatusCode);
        var visibleEmployee = await client.PostAsJsonAsync("/api/hr/v1/exits", new
        {
            employeeId = 1, exitType = "Resignation", initiatedDate = today,
            proposedLastWorkingDate = today, reason = "Allowed primary location."
        });
        Assert.Equal(HttpStatusCode.Created, visibleEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/hr/v1/exits", new
        {
            employeeId = 2, exitType = "Resignation", initiatedDate = today,
            proposedLastWorkingDate = today, reason = "Only additional assignment is in scope."
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/hr/v1/exits/1")).StatusCode);

        using var disabledFactory = new SqliteRoleGovernanceApiFactory();
        await Seed(disabledFactory.Services, hr: false);
        using var disabled = disabledFactory.CreateClient();
        Auth(disabled, "hr.exit.view,hr.exit.initiate,hr.exit.clearance");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await disabled.GetAsync("/api/hr/v1/exits/templates")).StatusCode);
    }

    [Fact]
    public async Task CoreHrReportsArePermissionAndCapabilityScopedAndExcludeSensitiveFields()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services, global: false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Outside outlet" });
            db.OperationalLocationProjections.Add(new OperationalLocationProjection
            {
                Id = 11, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
                SourceEntityId = "11", Name = "Outside outlet", SourceVersion = 1
            });
            db.UserOutletAssignments.Add(new UserOutletAssignment
            {
                UserId = 101, BusinessId = 1, OutletId = 10, IsPrimary = true
            });
            db.HrEmployees.Add(new HrEmployee
            {
                Id = 2, BusinessId = 1, EmployeeCode = "EMP-2", FirstName = "Outside",
                DisplayName = "Outside employee", CurrentEmploymentTypeId = 1,
                CurrentPrimaryLocationProjectionId = 11, JoiningDate = new DateOnly(2026, 2, 1),
                NationalId = "SENSITIVE-IDENTIFIER", Mobile = "555-0101",
                PersonalEmail = "private@example.invalid"
            });
            await db.SaveChangesAsync();
        }
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 12, 31);
        using var authorized = factory.CreateClient();
        Auth(authorized, "hr.report.view,hr.attendance.report");

        var rosterResponse = await authorized.GetAsync(
            $"/api/hr/v1/reports/core?kind=EmployeeRoster&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.True(rosterResponse.StatusCode == HttpStatusCode.OK,
            await rosterResponse.Content.ReadAsStringAsync());
        var roster = (await rosterResponse.Content.ReadFromJsonAsync<HrCoreReportPage>())!;
        Assert.Single(roster.Items);
        Assert.Equal("EMP", roster.Items[0].EmployeeCode);
        Assert.Equal("Outlet", roster.Items[0].LocationName);
        var json = await rosterResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("nationalId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mobile", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("personalEmail", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENSITIVE-IDENTIFIER", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Outside employee", json, StringComparison.Ordinal);

        var sorted = await authorized.GetAsync(
            $"/api/hr/v1/reports/core?kind=EmployeeRoster&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&sortBy=employeeCode&descending=false");
        Assert.True(sorted.StatusCode == HttpStatusCode.OK,
            await sorted.Content.ReadAsStringAsync());

        foreach (var kind in new[]
                 {
                     "EmployeeMaster", "Lifecycle", "LifecycleHistory", "PositionAssignmentHistory",
                     "LocationAssignmentHistory", "AttendanceSummary", "AttendanceExceptions",
                     "AttendanceCorrections",
                     "LeaveSummary", "LeaveBalance", "OvertimeSummary", "DocumentExpiry", "ExitClearance"
                 })
        {
            var response = await authorized.GetAsync(
                $"/api/hr/v1/reports/core?kind={kind}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"{kind}: {await response.Content.ReadAsStringAsync()}");
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await authorized.GetAsync(
            $"/api/hr/v1/reports/core?kind=EmployeeRoster&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&locationProjectionId=20"))
            .StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await authorized.GetAsync(
            $"/api/hr/v1/reports/core?kind=Untrusted&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"))
            .StatusCode);

        using var wrongPermission = factory.CreateClient();
        Auth(wrongPermission, "hr.employee.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongPermission.GetAsync(
            $"/api/hr/v1/reports/core?kind=EmployeeRoster&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"))
            .StatusCode);
        using var attendanceDenied = factory.CreateClient();
        Auth(attendanceDenied, "hr.report.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await attendanceDenied.GetAsync(
            $"/api/hr/v1/reports/core?kind=AttendanceCorrections&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"))
            .StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await authorized.GetAsync(
            $"/api/hr/v1/reports/core?kind=LeaveBalance&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&asOfDate=2027-01-01"))
            .StatusCode);

        using var disabledFactory = new SqliteRoleGovernanceApiFactory();
        await Seed(disabledFactory.Services, hr: false);
        using var disabled = disabledFactory.CreateClient();
        Auth(disabled, "hr.report.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await disabled.GetAsync(
            $"/api/hr/v1/reports/core?kind=EmployeeRoster&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"))
            .StatusCode);
    }

    [Fact]
    public async Task CoreHrReportAccessFailsClosedWhenRequiredAuditCannotBePersisted()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "View";

        using var client = factory.CreateClient();
        Auth(client, "hr.report.view");
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 12, 31);
        var response = await client.GetAsync(
            $"/api/hr/v1/reports/core?kind=EmployeeRoster&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("Employee", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.AuditEvents.Where(row =>
            row.Module == "Reports" && row.ActionType == "View").ToListAsync());
    }

    [Fact]
    public async Task CoreHrReportsReturnPersistedLifecycleLocationLeaveAndDocumentEvidence()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var leaveYearStart = new DateOnly(today.Year, 1, 1);
        var leaveYearEnd = new DateOnly(today.Year, 12, 31);
        var expiryDate = today.AddDays(1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.HrEmployeeLifecycleEvents.Add(new HrEmployeeLifecycleEvent
            {
                BusinessId = 1,
                EmployeeId = 1,
                EmploymentPeriodId = 1,
                EventType = "Promoted",
                EffectiveDate = today,
                PreviousStateJson = "{}",
                NewStateJson = "{}",
                Reason = "Lifecycle report evidence",
                ActorUserId = 101,
                OccurredAtUtc = DateTime.UtcNow
            });
            var documentType = new HrDocumentType
            {
                BusinessId = 1, Code = "ID", Name = "Identity document", RequiresExpiryDate = true
            };
            db.HrDocumentTypes.Add(documentType);
            await db.SaveChangesAsync();
            db.HrEmployeeDocuments.AddRange(
                new HrEmployeeDocument
                {
                    Id = 1, BusinessId = 1, EmployeeId = 1, DocumentTypeId = documentType.Id,
                    DocumentNumber = "CONFIDENTIAL-123", ExpiryDate = expiryDate
                },
                new HrEmployeeDocument
                {
                    Id = 2, BusinessId = 1, EmployeeId = 1, DocumentTypeId = documentType.Id,
                    DocumentNumber = "NO-EXPIRY"
                });
            var leaveType = new HrLeaveType
            {
                BusinessId = 1, Code = "ANNUAL", Name = "Annual leave", IsPaid = true, IsActive = true
            };
            db.HrLeaveTypes.Add(leaveType);
            await db.SaveChangesAsync();
            var policy = new HrLeavePolicyVersion
            {
                BusinessId = 1, LeaveTypeId = leaveType.Id, Code = "ANNUAL-2026", Name = "Annual leave",
                ScopeType = HrLeavePolicyScopeType.Business, VersionNo = 1,
                AccrualMethod = HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart,
                AnnualEntitlement = 20m, LeaveYearStartMonth = 1, LeaveYearStartDay = 1,
                EffectiveFrom = leaveYearStart, IsActive = true
            };
            db.HrLeavePolicyVersions.Add(policy);
            await db.SaveChangesAsync();
            var leaveDay = new HrLeaveRequestDay
            {
                BusinessId = 1, EmployeeId = 1, WorkDate = today,
                OperationalLocationProjectionId = 10, PolicyVersionId = policy.Id, Units = 1.5m,
                DayClassification = "NormalWorkingDay", ShiftSnapshotJson = "{}", CalendarEvidenceJson = "{}"
            };
            db.HrLeaveRequests.Add(new HrLeaveRequest
            {
                BusinessId = 1, EmployeeId = 1, LeaveTypeId = leaveType.Id,
                ScopeLocationProjectionId = 10, StartDate = today, EndDate = today,
                RequestedUnits = 1.5m, Reason = "Annual leave", Status = "Approved",
                RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow,
                DecidedByUserId = 101, DecidedAtUtc = DateTime.UtcNow,
                Days = [leaveDay]
            });
            db.HrLeaveEntitlements.Add(new HrLeaveEntitlement
            {
                BusinessId = 1, EmployeeId = 1, LeaveTypeId = leaveType.Id, PolicyVersionId = policy.Id,
                LeaveYearStart = leaveYearStart, LeaveYearEnd = leaveYearEnd,
                JoiningDateSnapshot = leaveYearStart, PrimaryLocationProjectionIdSnapshot = 10
            });
            await db.SaveChangesAsync();
            var entitlement = await db.HrLeaveEntitlements.SingleAsync();
            db.HrLeaveLedgerEntries.AddRange(
                new HrLeaveLedgerEntry
                {
                    BusinessId = 1, EntitlementId = entitlement.Id, EmployeeId = 1, LeaveTypeId = leaveType.Id,
                    PolicyVersionId = policy.Id, TransactionType = HrLeaveLedgerTransactionType.Opening,
                    Quantity = 5m, EffectiveDate = new DateOnly(2026, 1, 1), SourceType = "Test",
                    SourceId = "opening", IdempotencyKey = "report-opening", PayloadHash = new('A', 64),
                    Reason = "Persisted opening balance", ActorRealUserId = 101, CreatedAtUtc = DateTime.UtcNow
                },
                new HrLeaveLedgerEntry
                {
                    BusinessId = 1, EntitlementId = entitlement.Id, EmployeeId = 1, LeaveTypeId = leaveType.Id,
                    PolicyVersionId = policy.Id, TransactionType = HrLeaveLedgerTransactionType.Consumption,
                    Quantity = -1.5m, EffectiveDate = today, SourceType = "Test",
                    SourceId = "consumption", IdempotencyKey = "report-consumption", PayloadHash = new('B', 64),
                    Reason = "Persisted leave consumption", ActorRealUserId = 101, CreatedAtUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();

            var clearanceTemplate = new HrClearanceTemplate
            {
                    BusinessId = 1, Code = "REPORT-EXIT", Name = "Report exit checklist",
                    ScopeType = HrClearanceTemplateScopeType.Business, VersionNo = 1,
                    EffectiveFrom = today, IsActive = true, CreatedByUserId = 101,
                    Items =
                    [
                        new HrClearanceTemplateItem
                        {
                            BusinessId = 1, Sequence = 1, Code = "ASSET", Name = "Return assets",
                            IsMandatory = true
                        }
                    ]
            };
            db.HrClearanceTemplates.Add(clearanceTemplate);
            await db.SaveChangesAsync();
            db.HrExitCases.Add(new HrExitCase
            {
                    BusinessId = 1, EmployeeId = 1, ExitType = "Resignation", InitiatedDate = today,
                    ProposedLastWorkingDate = today.AddDays(14), Reason = "Private reason omitted from report",
                    Status = "ClearanceInProgress", InitiatedByUserId = 101, InitiatedAtUtc = DateTime.UtcNow,
                    PrimaryLocationProjectionIdSnapshot = 10, ClearanceTemplateId = clearanceTemplate.Id,
                    ClearanceTemplateVersionNo = 1, ClearanceTemplateSnapshotJson = "{}",
                    ClearanceItems =
                    [
                        new HrClearanceItem
                        {
                            BusinessId = 1, Sequence = 1, Code = "ASSET", Name = "Return assets",
                            IsMandatory = true, Status = "Pending"
                        },
                        new HrClearanceItem
                        {
                            BusinessId = 1, Sequence = 2, Code = "ACCESS", Name = "Revoke access",
                            IsMandatory = true, Status = "Cleared", ClearedByUserId = 101,
                            ClearedAtUtc = DateTime.UtcNow
                        }
                    ]
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        Auth(client, "hr.report.view,hr.attendance.report,hr.shift.manage,hr.roster.manage,hr.roster.view,hr.attendance.view,hr.attendance.reprocess");
        var from = today.AddDays(-1);
        var to = today.AddDays(2);

        var lifecycle = await client.GetFromJsonAsync<HrCoreReportPage>(
            $"/api/hr/v1/reports/core?kind=LifecycleHistory&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        var lifecycleRow = Assert.Single(lifecycle!.Items);
        Assert.Equal("Promoted", lifecycleRow.Category);
        Assert.Equal(101, lifecycleRow.ActorUserId);

        var locations = await client.GetFromJsonAsync<HrCoreReportPage>(
            $"/api/hr/v1/reports/core?kind=LocationAssignmentHistory&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.Equal("Outlet", Assert.Single(locations!.Items).LocationName);

        var leaves = await client.GetFromJsonAsync<HrCoreReportPage>(
            $"/api/hr/v1/reports/core?kind=LeaveSummary&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.Equal(1.5m, Assert.Single(leaves!.Items).Quantity);

        var balancesResponse = await client.GetAsync(
            $"/api/hr/v1/reports/core?kind=LeaveBalance&from={leaveYearStart:yyyy-MM-dd}&to={leaveYearEnd:yyyy-MM-dd}&asOfDate={today:yyyy-MM-dd}&status=Open");
        Assert.True(balancesResponse.StatusCode == HttpStatusCode.OK,
            await balancesResponse.Content.ReadAsStringAsync());
        var balances = await balancesResponse.Content.ReadFromJsonAsync<HrCoreReportPage>();
        var balance = Assert.Single(balances!.Items);
        Assert.Equal(3.5m, balance.Measures!.Balance);
        Assert.Equal(1.5m, balance.Measures.Used);
        Assert.Equal("Outlet", balance.LocationName);

        var shiftResponse = await client.PostAsJsonAsync(
            "/api/hr/v1/shifts", HrCalendarPersistenceTests.Shift());
        Assert.True(shiftResponse.StatusCode == HttpStatusCode.Created,
            await shiftResponse.Content.ReadAsStringAsync());
        var shift = (await shiftResponse.Content.ReadFromJsonAsync<HrShiftResponse>())!;
        var workDate = today.AddDays(1);
        var rosterResponse = await client.PostAsJsonAsync(
            "/api/hr/v1/rosters", new HrRosterData(1, workDate, shift.Id, 10));
        Assert.True(rosterResponse.StatusCode == HttpStatusCode.Created,
            await rosterResponse.Content.ReadAsStringAsync());
        var roster = (await rosterResponse.Content.ReadFromJsonAsync<HrRosterResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/hr/v1/rosters/{roster.Id}/publish", new { expectedVersion = roster.Version })).StatusCode);
        var attendanceResponse = await client.PostAsJsonAsync(
            "/api/hr/v1/attendance/process", new { From = workDate, To = workDate });
        Assert.Equal(HttpStatusCode.OK, attendanceResponse.StatusCode);
        long attendanceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var attendance = await db.Set<HrDailyAttendance>().SingleAsync(row =>
                row.BusinessId == 1 && row.EmployeeId == 1 && row.WorkDate == workDate);
            attendanceId = attendance.Id;
            attendance.Status = "MissingPunch";
            attendance.FirstInAtLocal = workDate.ToDateTime(new TimeOnly(9, 0));
            attendance.LateMinutes = 15;
            attendance.WorkedMinutes = 240;

            var approvalPolicy = new ApprovalPolicy
            {
                BusinessId = 1, Code = "REPORT-OVERTIME", ProcessType = ApprovalProcessType.Overtime,
                Name = "Report overtime approval", VersionNo = 1, ScopeType = ApprovalScopeType.Business,
                EffectiveFrom = today, IsActive = true
            };
            db.ApprovalPolicies.Add(approvalPolicy);
            var overtimePolicy = new HrOvertimePolicyVersion
            {
                BusinessId = 1, Code = "REPORT-OT", Name = "Report overtime policy",
                ScopeType = HrOvertimePolicyScopeType.Business, VersionNo = 1, EffectiveFrom = today,
                MinimumEligibleMinutes = 1, RoundingUnitMinutes = 1,
                RoundingMethod = HrOvertimeRoundingMethod.Down, AllowNormalWorkingDay = true,
                RateType = HrOvertimeRateType.Multiplier, NormalDayRate = 1m,
                WeeklyOffRate = 1m, HolidayRate = 1m, SpecialDayRate = 1m, IsActive = true
            };
            db.Set<HrOvertimePolicyVersion>().Add(overtimePolicy);
            await db.SaveChangesAsync();
            var approval = new ApprovalInstance
            {
                BusinessId = 1, PolicyId = approvalPolicy.Id, PolicyCodeSnapshot = approvalPolicy.Code,
                PolicyVersionSnapshot = 1, ProcessType = ApprovalProcessType.Overtime,
                TargetType = "HrOvertimeEntry", TargetId = "report-overtime",
                RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow,
                Status = ApprovalInstanceStatus.Approved, CorrelationId = Guid.NewGuid(),
                ScopeType = ApprovalScopeType.Business, TargetVersion = 1, RequestHash = new('C', 64)
            };
            db.ApprovalInstances.Add(approval);
            await db.SaveChangesAsync();
            db.HrOvertimeEntries.Add(new HrOvertimeEntry
            {
                BusinessId = 1, EmployeeId = 1, WorkDate = workDate, DailyAttendanceId = attendance.Id,
                OperationalLocationProjectionId = 10, DayCategory = "NormalWorkingDay",
                AttendanceProcessingVersion = attendance.ProcessingVersion, PolicyVersionId = overtimePolicy.Id,
                PolicyVersionNo = 1, EligibleMinutes = 30, CalculatedMinutes = 30, ApprovedMinutes = 30,
                Status = "PayrollEligible", CalculationVersion = 1,
                CalculationFingerprint = new('D', 64), RequestHash = new('E', 64),
                PolicySnapshotJson = "{}", AttendanceSnapshotJson = "{}", CalculationInputsJson = "{}",
                ApprovalInstanceId = approval.Id
            });
            await db.SaveChangesAsync();
        }
        var attendanceSummaryResponse = await client.GetAsync(
            $"/api/hr/v1/reports/core?kind=AttendanceSummary&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.True(attendanceSummaryResponse.StatusCode == HttpStatusCode.OK,
            await attendanceSummaryResponse.Content.ReadAsStringAsync());
        var attendanceSummary = await attendanceSummaryResponse.Content.ReadFromJsonAsync<HrCoreReportPage>();
        var attendanceRow = Assert.Single(attendanceSummary!.Items);
        Assert.Equal(1, attendanceRow.Measures!.ScheduledDays);
        Assert.Equal(1, attendanceRow.Measures.PresentDays);
        Assert.Equal(1, attendanceRow.Measures.LateDays);
        Assert.Equal(240, attendanceRow.Measures.WorkedMinutes);
        Assert.Equal(1, attendanceRow.Measures.ApprovedOvertimeEntries);
        var attendanceExceptions = await client.GetFromJsonAsync<HrCoreReportPage>(
            $"/api/hr/v1/reports/core?kind=AttendanceExceptions&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.Equal("MissingPunch", Assert.Single(attendanceExceptions!.Items).Status);
        var overtime = await client.GetFromJsonAsync<HrCoreReportPage>(
            $"/api/hr/v1/reports/core?kind=OvertimeSummary&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        var overtimeRow = Assert.Single(overtime!.Items);
        Assert.Equal("PayrollEligible", overtimeRow.Status);
        Assert.Equal(30, overtimeRow.Metric);

        var clearance = await client.GetFromJsonAsync<HrCoreReportPage>(
            $"/api/hr/v1/reports/core?kind=ExitClearance&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        var clearanceRow = Assert.Single(clearance!.Items);
        Assert.Equal(1, clearanceRow.Metric);
        Assert.Equal(2, clearanceRow.Measures!.MandatoryClearanceItems);
        Assert.Equal(1, clearanceRow.Measures.CompletedMandatoryClearanceItems);
        Assert.DoesNotContain("Private reason omitted from report",
            await client.GetStringAsync(
                $"/api/hr/v1/reports/core?kind=ExitClearance&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"),
            StringComparison.Ordinal);

        var documentsResponse = await client.GetAsync(
            $"/api/hr/v1/reports/core?kind=DocumentExpiry&from={today:yyyy-MM-dd}&to={to:yyyy-MM-dd}&asOfDate={today:yyyy-MM-dd}");
        Assert.True(documentsResponse.StatusCode == HttpStatusCode.OK,
            await documentsResponse.Content.ReadAsStringAsync());
        var documents = await documentsResponse.Content.ReadFromJsonAsync<HrCoreReportPage>();
        var document = Assert.Single(documents!.Items);
        Assert.Equal("Expiring", document.Status);
        Assert.Equal(1, document.Metric);
        var documentJson = await documentsResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("CONFIDENTIAL-123", documentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("NO-EXPIRY", documentJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CoreHrReportExportsAreBoundedAuditedAndFormulaSafe()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var employee = await db.HrEmployees.SingleAsync(row => row.BusinessId == 1);
            employee.DisplayName = "=2+2";
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        Auth(client, "hr.report.view");
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 12, 31);
        var query = $"kind=EmployeeRoster&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

        var csv = await client.GetAsync($"/api/hr/v1/reports/core/export/csv?{query}");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var csvText = await csv.Content.ReadAsStringAsync();
        Assert.Contains("'=2+2", csvText, StringComparison.Ordinal);
        Assert.True(csv.Headers.CacheControl?.Private);
        Assert.True(csv.Headers.CacheControl?.NoStore);

        var xlsx = await client.GetAsync($"/api/hr/v1/reports/core/export/xlsx?{query}");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var xlsxBytes = await xlsx.Content.ReadAsByteArrayAsync();
        using (var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(xlsxBytes)))
        {
            var formulaLikeCell = workbook.Worksheet("HR Report").Cell(2, 3);
            Assert.Equal("=2+2", formulaLikeCell.GetString());
            Assert.Equal(ClosedXML.Excel.XLDataType.Text, formulaLikeCell.DataType);
            Assert.Empty(formulaLikeCell.FormulaA1);
        }

        using var denied = factory.CreateClient();
        Auth(denied, "hr.employee.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await denied.GetAsync($"/api/hr/v1/reports/core/export/csv?{query}")).StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var exportAudit = await verifyDb.AuditEvents
            .Where(row => row.Module == "Reports" && row.ActionType == "Export")
            .ToListAsync();
        Assert.Equal(2, exportAudit.Count);
        Assert.All(exportAudit, row =>
        {
            Assert.DoesNotContain("=2+2", row.ActionSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("=2+2", row.Metadata ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AttendanceCorrectionRoutesRequireSeparateCorrectionAndApprovalPermissions()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var authorized = factory.CreateClient();
        await Seed(factory.Services);
        Auth(authorized,
        "hr.shift.manage,hr.roster.manage,hr.roster.view,hr.attendance.view,hr.attendance.reprocess,hr.attendance.correct");

        var shiftResponse = await authorized.PostAsJsonAsync("/api/hr/v1/shifts", HrCalendarPersistenceTests.Shift());
        Assert.Equal(HttpStatusCode.Created, shiftResponse.StatusCode);
        var shift = (await shiftResponse.Content.ReadFromJsonAsync<HrShiftResponse>())!;
        var workDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var rosterResponse = await authorized.PostAsJsonAsync("/api/hr/v1/rosters",
        new HrRosterData(1, workDate, shift.Id, 10));
        Assert.Equal(HttpStatusCode.Created, rosterResponse.StatusCode);
        var roster = (await rosterResponse.Content.ReadFromJsonAsync<HrRosterResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await authorized.PostAsJsonAsync(
        $"/api/hr/v1/rosters/{roster.Id}/publish", new { expectedVersion = roster.Version })).StatusCode);
        var attendanceResponse = await authorized.PostAsJsonAsync("/api/hr/v1/attendance/process",
        new { From = workDate, To = workDate });
        Assert.Equal(HttpStatusCode.OK, attendanceResponse.StatusCode);
        var daily = Assert.Single((await attendanceResponse.Content
        .ReadFromJsonAsync<HrDailyAttendanceResponse[]>())!);

        var correctionBody = new RequestHrAttendanceCorrectionRequest
        {
        CorrectionId = Guid.NewGuid(),
        ExpectedProcessingVersion = daily.ProcessingVersion,
        WorkedMinutes = 0,
        BreakMinutes = 0,
        LateMinutes = 0,
        EarlyLeaveMinutes = 0,
        Status = "Absent",
        Reason = "API permission check"
        };
        using var noCorrectionPermission = factory.CreateClient();
        Auth(noCorrectionPermission, "hr.attendance.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await noCorrectionPermission.PostAsJsonAsync(
        $"/api/hr/v1/attendance/daily/{daily.Id}/corrections", correctionBody)).StatusCode);

        using var noApprovalPermission = factory.CreateClient();
        Auth(noApprovalPermission, "hr.attendance.correct");
        Assert.Equal(HttpStatusCode.Forbidden, (await noApprovalPermission.PostAsJsonAsync(
        "/api/hr/v1/attendance/corrections/approvals/1/approve",
        new DecideAttendanceCorrectionRequest { DecisionKey = "not-authorized" })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await authorized.PostAsJsonAsync(
        $"/api/hr/v1/attendance/daily/{daily.Id}/corrections", correctionBody)).StatusCode);
        Assert.Empty((await authorized.GetFromJsonAsync<HrAttendanceCorrectionResponse[]>(
        $"/api/hr/v1/attendance/daily/{daily.Id}/corrections"))!);
    }
    private static async Task Seed(IServiceProvider services, bool hr = true, bool attendance = true, bool global = true)
    {
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync(); await HrRosterPersistenceTests.Seed(db);
        db.BusinessFeatureSettings.AddRange(new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = hr },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "attendance.enabled", IsEnabled = attendance });
        db.Users.Add(new User { Id = 101, BusinessId = 1, Name = "Actor", Email = "roster@example.invalid", PasswordHash = "test",
            IsActive = true, InventoryLocationAccessScope = global ? User.InventoryAccessAll : User.InventoryAccessSpecific });
        await db.SaveChangesAsync();
    }
    private static void Auth(HttpClient c, string permissions)
    {
        c.DefaultRequestHeaders.Add("X-Test-UserId", "101"); c.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        c.DefaultRequestHeaders.Add("X-Test-Role", "HR"); c.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }
}
