using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrOpeningImportApiTests
{
    [Fact]
    public async Task PayrollTemplateUsesPayrollCapabilityAndImportPermission()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, "payroll.import.view");
        await SeedAsync(factory.Services, payrollEnabled: true);

        using var response = await client.GetAsync(
            "/api/payroll/v1/opening-imports/template?importType=Salary&templateVersion=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("SalaryStructureId", await response.Content.ReadAsStringAsync());

        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "payroll.salary.view");
        using var missingImportPermission = await client.GetAsync(
            "/api/payroll/v1/opening-imports/template?importType=Salary&templateVersion=1");
        Assert.Equal(HttpStatusCode.Forbidden, missingImportPermission.StatusCode);
    }

    [Fact]
    public async Task PayrollOpeningImportRequiresHrCapabilityPrerequisite()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, "payroll.import.view");
        await SeedAsync(factory.Services, enabled: false, payrollEnabled: true);

        using var response = await client.GetAsync(
            "/api/payroll/v1/opening-imports/template?importType=LoanOpening&templateVersion=1");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Salary")]
    [InlineData("LoanOpening")]
    public async Task PayrollOpeningImportHttpAuthorizationMatrix(string importType)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var composed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPrivateBlobStore>(new ImportTestBlobs());
            services.AddSingleton<IConfidentialFileEncryption>(new ImportTestEncryption());
            var inspector = new Mock<IConfidentialContentInspector>();
            inspector.Setup(i => i.ValidateAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConfidentialContentValidation("text/csv", ".csv"));
            inspector.Setup(i => i.ScanAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConfidentialFileScanStatus.Clean);
            services.AddSingleton(inspector.Object);
            services.AddSingleton(new ConfidentialFileLimits(100_000, 100_000));
            services.AddSingleton(new HrImportPolicy(100, 365));
        }));
        await SeedPayrollImportAsync(composed.Services);
        using var authorized = PayrollClient(composed, 101, 1,
            "payroll.import.view,payroll.import.execute,payroll.salary.view,payroll.salary.manage,payroll.loan.view,payroll.loan.manage");
        using var missingImport = PayrollClient(composed, 101, 1,
            importType == "Salary" ? "payroll.salary.manage" : "payroll.loan.manage");
        using var missingManage = PayrollClient(composed, 101, 1,
            "payroll.import.view,payroll.import.execute,payroll.salary.view,payroll.loan.view");
        using var unauthorizedLocation = PayrollClient(composed, 404, 1,
            "payroll.import.view,payroll.import.execute,payroll.salary.view,payroll.salary.manage,payroll.loan.view,payroll.loan.manage");
        using var additionalOnlyLocation = PayrollClient(composed, 303, 1,
            "payroll.import.view,payroll.import.execute,payroll.salary.view,payroll.salary.manage,payroll.loan.view,payroll.loan.manage");

        var csv = await PayrollCsvAsync(composed.Services, importType, "EMP-PRIMARY");
        var path = "/api/payroll/v1/opening-imports";

        using var noImportUpload = await missingImport.PostAsync(
            path, Form(importType, csv, Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Forbidden, noImportUpload.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(noImportUpload);
        using var noManageUpload = await missingManage.PostAsync(
            path, Form(importType, csv, Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Forbidden, noManageUpload.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(noManageUpload);
        using var locationDeniedUpload = await unauthorizedLocation.PostAsync(
            path, Form(importType, csv, Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Forbidden, locationDeniedUpload.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(locationDeniedUpload);
        using var additionalOnlyDeniedUpload = await additionalOnlyLocation.PostAsync(
            path, Form(importType, csv, Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Forbidden, additionalOnlyDeniedUpload.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(additionalOnlyDeniedUpload);

        var foreignCsv = await PayrollCsvAsync(composed.Services, importType, "FOREIGN-EMP");
        using var foreignUpload = await authorized.PostAsync(
            path, Form(importType, foreignCsv, Guid.NewGuid().ToString("N")));
        var foreignBody = await foreignUpload.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, foreignUpload.StatusCode);
        Assert.DoesNotContain("FOREIGN-EMP", foreignBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageKey", foreignBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE-", foreignBody, StringComparison.Ordinal);

        await SetPayrollCapabilityAsync(composed.Services, "hr.enabled", false);
        using var hrDisabledUpload = await authorized.PostAsync(
            path, Form(importType, csv, Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Forbidden, hrDisabledUpload.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(hrDisabledUpload);
        await SetPayrollCapabilityAsync(composed.Services, "hr.enabled", true);
        await SetPayrollCapabilityAsync(composed.Services, "payroll.enabled", false);
        using var payrollDisabledUpload = await authorized.PostAsync(
            path, Form(importType, csv, Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Forbidden, payrollDisabledUpload.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(payrollDisabledUpload);
        await SetPayrollCapabilityAsync(composed.Services, "payroll.enabled", true);
        await AssertNoOpeningImportMutationsAsync(composed.Services);

        using var uploaded = await authorized.PostAsync(
            path, Form(importType, csv, Guid.NewGuid().ToString("N")));
        var uploadBody = await uploaded.Content.ReadAsStringAsync();
        Assert.True(uploaded.StatusCode == HttpStatusCode.Created, uploadBody);
        Assert.DoesNotContain("StorageKey", uploadBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opening.csv", uploadBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EMP-PRIMARY", uploadBody, StringComparison.Ordinal);
        var batch = (await uploaded.Content.ReadFromJsonAsync<HrImportBatchResult>())!;

        using var detail = await authorized.GetAsync($"{path}/{batch.Id}");
        var detailBody = await detail.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.True(detail.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain("StorageKey", detailBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opening.csv", detailBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EMP-PRIMARY", detailBody, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-", detailBody, StringComparison.Ordinal);
        Assert.DoesNotContain("1250", detailBody, StringComparison.Ordinal);
        Assert.DoesNotContain("1000", detailBody, StringComparison.Ordinal);
        Assert.DoesNotContain("600", detailBody, StringComparison.Ordinal);

        using var readyDryRun = await authorized.PostAsJsonAsync($"{path}/{batch.Id}/dry-run", new { });
        var readyDryRunBody = await readyDryRun.Content.ReadAsStringAsync();
        Assert.True(readyDryRun.StatusCode == HttpStatusCode.OK, readyDryRunBody);
        Assert.Equal("Ready", (await readyDryRun.Content.ReadFromJsonAsync<HrImportBatchResult>())!.Status);
        using var deniedDryRun = await missingManage.PostAsJsonAsync($"{path}/{batch.Id}/dry-run", new { });
        Assert.Equal(HttpStatusCode.Forbidden, deniedDryRun.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(deniedDryRun);
        using var deniedExecute = await missingManage.PostAsJsonAsync($"{path}/{batch.Id}/execute", new { });
        Assert.Equal(HttpStatusCode.Forbidden, deniedExecute.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(deniedExecute);
        using var noImportDryRun = await missingImport.PostAsJsonAsync($"{path}/{batch.Id}/dry-run", new { });
        Assert.Equal(HttpStatusCode.Forbidden, noImportDryRun.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(noImportDryRun);
        using var noImportExecute = await missingImport.PostAsJsonAsync($"{path}/{batch.Id}/execute", new { });
        Assert.Equal(HttpStatusCode.Forbidden, noImportExecute.StatusCode);
        await AssertNoSensitiveImportEvidenceAsync(noImportExecute);
        await AssertNoOpeningRecordsAsync(composed.Services);
        var unchanged = await authorized.GetFromJsonAsync<HrImportBatchResult>($"{path}/{batch.Id}");
        Assert.Equal("Ready", unchanged!.Status);

        using var execution = await authorized.PostAsJsonAsync($"{path}/{batch.Id}/execute", new { });
        var executionBody = await execution.Content.ReadAsStringAsync();
        Assert.True(execution.StatusCode == HttpStatusCode.OK, executionBody);
        Assert.DoesNotContain("StorageKey", executionBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opening.csv", executionBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EMP-PRIMARY", executionBody, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-", executionBody, StringComparison.Ordinal);
        Assert.Equal("Completed", (await execution.Content.ReadFromJsonAsync<HrImportBatchResult>())!.Status);

        using var scope = composed.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(1, await db.HrImportBatches.CountAsync());
        Assert.Equal(1, importType == "Salary"
            ? await db.PayrollSalarySetupRequests.CountAsync()
            : await db.PayrollEmployeeLoans.CountAsync());
        Assert.Empty(await db.PayrollEmployeeSalaryVersions.ToArrayAsync());
        if (importType == "LoanOpening")
        {
            var loan = await db.PayrollEmployeeLoans.SingleAsync();
            Assert.Equal("Draft", loan.Status);
            Assert.Null(loan.DisbursedDate);
            Assert.Equal(600m, loan.OpeningBalanceAmount);
        }
        Assert.True(await db.AuditEvents.AnyAsync(a => a.ActionType == "payroll.import.uploaded"));
        Assert.True(await db.AuditEvents.AnyAsync(a => a.ActionType == "payroll.import.dry_run"));
        Assert.True(await db.AuditEvents.AnyAsync(a => a.ActionType == "payroll.import.executed"));
    }

    [Fact]
    public async Task HttpWorkflowRequiresDryRunAndReturnsSafeIdempotentEvidence()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var composed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPrivateBlobStore>(new ImportTestBlobs());
            services.AddSingleton<IConfidentialFileEncryption>(new ImportTestEncryption());
            var inspector = new Mock<IConfidentialContentInspector>();
            inspector.Setup(i => i.ValidateAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConfidentialContentValidation("text/csv", ".csv"));
            inspector.Setup(i => i.ScanAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConfidentialFileScanStatus.Clean);
            services.AddSingleton(inspector.Object);
            services.AddSingleton(new ConfidentialFileLimits(100_000, 100_000));
            services.AddSingleton(new HrImportPolicy(100, 365));
        }));
        using var client = composed.CreateClient();
        Authorize(client, "hr.import.view,hr.import.execute,hr.employee.create");
        await SeedAsync(composed.Services);
        using var template = await client.GetAsync("/api/hr/v1/opening-imports/template");
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.True(template.Headers.CacheControl!.NoStore);
        var csv = await template.Content.ReadAsStringAsync() +
            $"external,HTTP-IMPORT,Alex,Alex Employee,11,Active,{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd},,,,,,";
        var key = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "hr.import.view,hr.import.execute");
        using var deniedCreate = await client.PostAsync("/api/hr/v1/opening-imports", Form(csv, key));
        Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "hr.import.view,hr.import.execute,hr.employee.create");
        using var uploaded = await client.PostAsync("/api/hr/v1/opening-imports", Form(csv, key));
        var body = await uploaded.Content.ReadAsStringAsync();
        Assert.True(uploaded.StatusCode == HttpStatusCode.Created, body);
        Assert.NotNull(uploaded.Headers.Location);
        Assert.DoesNotContain("storageKey", body, StringComparison.OrdinalIgnoreCase);
        var batch = (await uploaded.Content.ReadFromJsonAsync<HrImportBatchResult>())!;
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"/api/hr/v1/opening-imports/{batch.Id}/execute", new { })).StatusCode);
        using var dryRun = await client.PostAsJsonAsync($"/api/hr/v1/opening-imports/{batch.Id}/dry-run", new { });
        Assert.Equal(HttpStatusCode.OK, dryRun.StatusCode);
        Assert.Equal("Ready", (await dryRun.Content.ReadFromJsonAsync<HrImportBatchResult>())!.Status);
        using (var scope = composed.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>().HrEmployees.AnyAsync());
        using var execution = await client.PostAsJsonAsync($"/api/hr/v1/opening-imports/{batch.Id}/execute", new { });
        Assert.Equal(HttpStatusCode.OK, execution.StatusCode);
        Assert.Equal("Completed", (await execution.Content.ReadFromJsonAsync<HrImportBatchResult>())!.Status);
        using var replay = await client.PostAsync("/api/hr/v1/opening-imports", Form(csv, key));
        Assert.Equal(batch.Id, (await replay.Content.ReadFromJsonAsync<HrImportBatchResult>())!.Id);
        using var get = await client.GetAsync($"/api/hr/v1/opening-imports/{batch.Id}");
        Assert.True(get.Headers.CacheControl!.NoStore);
        using var evidenceScope = composed.Services.CreateScope();
        var db = evidenceScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(1, await db.HrEmployees.CountAsync());
        Assert.True(await db.AuditEvents.AnyAsync(a => a.ActionType == "hr.import.executed"));
    }

    [Theory]
    [InlineData("", true, HttpStatusCode.Forbidden)]
    [InlineData("hr.import.execute", false, HttpStatusCode.Forbidden)]
    [InlineData("hr.import.execute,hr.employee.create", true, HttpStatusCode.ServiceUnavailable)]
    public async Task ProductionCompositionAndCapabilityRemainFailClosed(string permissions, bool enabled, HttpStatusCode expected)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, permissions);
        await SeedAsync(factory.Services, enabled);
        using var response = await client.PostAsync("/api/hr/v1/opening-imports", Form("not accepted", "stable"));
        Assert.Equal(expected, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(await db.HrImportBatches.AnyAsync());
        Assert.False(await db.HrEmployees.AnyAsync());
    }

    private static MultipartFormDataContent Form(string csv, string key) =>
        Form("Employee", csv, key);

    private static MultipartFormDataContent Form(string importType, string csv, string key)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(importType), "ImportType");
        form.Add(new StringContent("1"), "TemplateVersion");
        form.Add(new StringContent(key), "IdempotencyKey");
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "File", "opening.csv");
        return form;
    }

    private static HttpClient PayrollClient(
        WebApplicationFactory<Program> factory, long userId, long businessId, string permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Payroll");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static void Authorize(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessOwner");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }

    private static async Task SeedAsync(
        IServiceProvider services, bool enabled = true, bool payrollEnabled = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Import API", IsActive = true });
        db.BusinessFeatureSettings.Add(new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = enabled });
        if (payrollEnabled)
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1, FeatureKey = "payroll.enabled", IsEnabled = true
            });
        db.Users.Add(new User { Id = 101, BusinessId = 1, Name = "Import actor", Email = "import@example.invalid",
            PasswordHash = "test", IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAll });
        db.HrEmploymentTypes.Add(new HrEmploymentType { Id = 11, BusinessId = 1, Code = "PERM", Name = "Permanent", IsActive = true });
        await db.SaveChangesAsync();
    }

    private static async Task SeedPayrollImportAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Payroll Import", IsActive = true },
            new Business { Id = 2, Name = "Other Payroll", IsActive = true });
        db.BusinessFeatureSettings.AddRange(
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = true },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "payroll.enabled", IsEnabled = true });
        db.Outlets.AddRange(
            new Outlet { Id = 10, BusinessId = 1, Name = "Primary" },
            new Outlet { Id = 20, BusinessId = 1, Name = "Additional" },
            new Outlet { Id = 40, BusinessId = 1, Name = "Unauthorized" },
            new Outlet { Id = 30, BusinessId = 2, Name = "Foreign" });
        db.HrEmploymentTypes.AddRange(
            new HrEmploymentType { Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent", IsActive = true },
            new HrEmploymentType { Id = 2, BusinessId = 2, Code = "PERM", Name = "Permanent", IsActive = true });
        db.Users.AddRange(
            ImportUser(101, 1, 10, "Primary location"),
            ImportUser(303, 1, 20, "Additional-only location"),
            ImportUser(404, 1, 40, "Unauthorized location"),
            ImportUser(201, 2, 30, "Foreign Business"));
        db.UserOutletAssignments.AddRange(
            ImportUserOutlet(101, 1, 10),
            ImportUserOutlet(303, 1, 20),
            ImportUserOutlet(404, 1, 40),
            ImportUserOutlet(201, 2, 30));
        db.OperationalLocationProjections.AddRange(
            ImportLocation(10, 1, 10),
            ImportLocation(20, 1, 20),
            ImportLocation(30, 2, 30),
            ImportLocation(40, 1, 40));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var employee = ImportEmployee(1, 1, "EMP-PRIMARY", 10, today);
        var foreignEmployee = ImportEmployee(2, 2, "FOREIGN-EMP", 30, today);
        db.HrEmployees.AddRange(employee, foreignEmployee);
        await db.SaveChangesAsync();
        db.HrEmploymentPeriods.AddRange(
            ImportEmploymentPeriod(1, employee.Id, 1, today),
            ImportEmploymentPeriod(2, foreignEmployee.Id, 2, today));
        db.HrEmployeeLocationAssignments.AddRange(
            ImportEmployeeLocation(employee.Id, 1, 10, "Primary", today),
            ImportEmployeeLocation(employee.Id, 1, 20, "Additional", today),
            ImportEmployeeLocation(foreignEmployee.Id, 2, 30, "Primary", today));
        var component = new PayrollSalaryComponent
        {
            BusinessId = 1, Code = "OPEN_BASE", Name = "Opening base",
            ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true, CalculationOrder = 1
        };
        db.PayrollSalaryComponents.Add(component);
        await db.SaveChangesAsync();
        db.PayrollSalaryStructures.Add(new PayrollSalaryStructure
        {
            BusinessId = 1, Code = "OPEN_STANDARD", Name = "Opening standard",
            Lines =
            [
                new PayrollSalaryStructureLine
                {
                    BusinessId = 1, SalaryComponentId = component.Id,
                    DefaultValue = 1000m, CalculationOrder = 1
                }
            ]
        });
        await db.SaveChangesAsync();
    }

    private static User ImportUser(long id, long businessId, long outletId, string name) => new()
    {
        Id = id, BusinessId = businessId, Name = name, Email = $"import-{id}@example.invalid",
        PasswordHash = "test", IsActive = true, OutletId = outletId,
        InventoryLocationAccessScope = User.InventoryAccessAssignedOnly
    };

    private static UserOutletAssignment ImportUserOutlet(long userId, long businessId, long outletId) => new()
    {
        UserId = userId, BusinessId = businessId, OutletId = outletId, IsPrimary = true
    };

    private static OperationalLocationProjection ImportLocation(long id, long businessId, long outletId) => new()
    {
        Id = id, BusinessId = businessId, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
        SourceEntityId = outletId.ToString(), SourceVersion = 1, SourcePayloadHash = new string('A', 64),
        Name = "Import location", IsSourceActive = true, SyncStatus = "Current"
    };

    private static HrEmployee ImportEmployee(
        long id, long businessId, string code, long locationId, DateOnly today) => new()
    {
        Id = id, BusinessId = businessId, EmployeeCode = code, FirstName = "Import",
        DisplayName = code, CurrentEmploymentTypeId = businessId, CurrentEmploymentStatus = "Active",
        JoiningDate = today.AddYears(-1), CurrentPrimaryLocationProjectionId = locationId
    };

    private static HrEmploymentPeriod ImportEmploymentPeriod(
        long businessId, long employeeId, long employmentTypeId, DateOnly today) => new()
    {
        BusinessId = businessId, EmployeeId = employeeId, EmploymentTypeId = employmentTypeId,
        StartDate = today.AddYears(-1), Status = "Active"
    };

    private static HrEmployeeLocationAssignment ImportEmployeeLocation(
        long employeeId, long businessId, long locationId, string type, DateOnly today) => new()
    {
        BusinessId = businessId, EmployeeId = employeeId, OperationalLocationProjectionId = locationId,
        AssignmentType = type, EffectiveFrom = today.AddYears(-1), LocationNameSnapshot = "Import location",
        SourceSystemSnapshot = "RetailPOS", SourceEntityTypeSnapshot = "Outlet",
        SourceEntityIdSnapshot = locationId.ToString(), LocationSourceVersionSnapshot = 1,
        RequestedByUserId = businessId == 1 ? 101 : 201, RequestedAtUtc = DateTime.UtcNow
    };

    private static async Task<string> PayrollCsvAsync(
        IServiceProvider services, string importType, string employeeCode)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var parser = new HrOpeningImportCsvParser();
        if (importType == "Salary")
        {
            var structureId = await db.PayrollSalaryStructures.Where(row => row.BusinessId == 1)
                .Select(row => row.Id).SingleAsync();
            var componentId = await db.PayrollSalaryComponents.Where(row => row.BusinessId == 1)
                .Select(row => row.Id).SingleAsync();
            return parser.Template(importType, "1")
                + $"salary-row,{employeeCode},{structureId},{today:yyyy-MM-dd},"
                + $"\"[{{\"\"salaryComponentId\"\":{componentId},\"\"value\"\":1250}}]\",Confidential salary reason,PRIVATE-SALARY-REF";
        }
        return parser.Template(importType, "1")
            + $"loan-row,{employeeCode},Advance,1000,600,{today.AddYears(-1):yyyy-MM-dd},"
            + $"{today:yyyy-MM-dd},FixedMonthly,200,,Confidential advance reason,PRIVATE-LOAN-REF";
    }

    private static async Task SetPayrollCapabilityAsync(
        IServiceProvider services, string featureKey, bool enabled)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var setting = await db.BusinessFeatureSettings.SingleAsync(row =>
            row.BusinessId == 1 && row.FeatureKey == featureKey);
        setting.IsEnabled = enabled;
        await db.SaveChangesAsync();
    }

    private static async Task AssertNoOpeningImportMutationsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.HrImportBatches.ToArrayAsync());
        await AssertNoOpeningRecordsAsync(db);
    }

    private static async Task AssertNoOpeningRecordsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await AssertNoOpeningRecordsAsync(scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>());
    }

    private static async Task AssertNoOpeningRecordsAsync(RetailPOSDbContext db)
    {
        Assert.Empty(await db.PayrollSalarySetupRequests.ToArrayAsync());
        Assert.Empty(await db.PayrollEmployeeLoans.ToArrayAsync());
    }

    private static async Task AssertNoSensitiveImportEvidenceAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("StorageKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opening.csv", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EMP-PRIMARY", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-", body, StringComparison.Ordinal);
        Assert.DoesNotContain("1250", body, StringComparison.Ordinal);
        Assert.DoesNotContain("1000", body, StringComparison.Ordinal);
        Assert.DoesNotContain("600", body, StringComparison.Ordinal);
    }
}
