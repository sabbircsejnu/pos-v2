using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Controllers.Payroll;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollSalarySetupApiTests
{
    [Fact]
    public async Task PaymentDestinationsAreEffectiveDatedConfidentialAndPrimaryLocationScoped()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await HrRosterPersistenceTests.Seed(db, businessExists: true);
            var maker = await db.Users.SingleAsync(row => row.Id == 101);
            maker.OutletId = 10;
            maker.InventoryLocationAccessScope = User.InventoryAccessAssignedOnly;
            db.Outlets.AddRange(
                new Outlet { Id = 20, BusinessId = 2, Name = "Foreign outlet" },
                new Outlet { Id = 30, BusinessId = 1, Name = "Additional outlet" });
            db.OperationalLocationProjections.Add(new OperationalLocationProjection
            {
                Id = 31, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
                SourceEntityId = "30", Name = "Additional outlet", SourceVersion = 1
            });
            db.Users.AddRange(
                new User
                {
                    Id = 303, BusinessId = 1, Name = "Additional-only user",
                    Email = "destination-additional@example.invalid", PasswordHash = "test",
                    IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                    OutletId = 30
                },
                new User
                {
                    Id = 201, BusinessId = 2, Name = "Foreign user",
                    Email = "destination-foreign@example.invalid", PasswordHash = "test",
                    IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                    OutletId = 20
                });
            db.UserOutletAssignments.AddRange(
                new UserOutletAssignment { UserId = 101, BusinessId = 1, OutletId = 10, IsPrimary = true },
                new UserOutletAssignment { UserId = 303, BusinessId = 1, OutletId = 30, IsPrimary = true },
                new UserOutletAssignment { UserId = 201, BusinessId = 2, OutletId = 20, IsPrimary = true });
            db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
            {
                BusinessId = 1, EmployeeId = 1, OperationalLocationProjectionId = 31,
                AssignmentType = "Additional", EffectiveFrom = new DateOnly(2026, 1, 1),
                LocationNameSnapshot = "Additional outlet", SourceSystemSnapshot = "RetailPOS",
                SourceEntityTypeSnapshot = "Outlet", SourceEntityIdSnapshot = "30",
                LocationSourceVersionSnapshot = 1, RequestedByUserId = 101,
                RequestedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        const string route = "/api/payroll/v1/employees/1/payment-destinations";
        var request = new CreatePayrollEmployeePaymentDestinationRequest(
            new PayrollEmployeePaymentDestinationData(
                "Bank", new DateOnly(2026, 10, 1), "Example Bank", "Employee",
                "123456789012", null, null, null));

        using var missingManage = factory.CreateClient();
        Auth(missingManage, "payroll.payment_destination.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingManage.PostAsJsonAsync(route, request)).StatusCode);

        using var makerClient = factory.CreateClient();
        Auth(makerClient, "payroll.payment_destination.manage");
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.payment_destination.created";
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await makerClient.PostAsJsonAsync(route, request)).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using (var auditRollbackScope = factory.Services.CreateScope())
        {
            var auditRollbackDb = auditRollbackScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await auditRollbackDb.PayrollEmployeePaymentDestinations.ToArrayAsync());
        }
        using var create = await makerClient.PostAsJsonAsync(route, request);
        Assert.True(create.StatusCode == HttpStatusCode.Created,
            await create.Content.ReadAsStringAsync());
        var created = (await create.Content.ReadFromJsonAsync<
            PayrollEmployeePaymentDestinationResponse>())!;
        Assert.Equal("****9012", created.AccountNumber);

        using var missingView = factory.CreateClient();
        Auth(missingView, "");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingView.GetAsync($"{route}/effective?effectiveOn=2026-10-05")).StatusCode);

        using var ordinaryReader = factory.CreateClient();
        Auth(ordinaryReader, "payroll.payment_destination.view");
        var masked = await ordinaryReader.GetFromJsonAsync<PayrollEmployeePaymentDestinationResponse>(
            $"{route}/effective?effectiveOn=2026-10-05");
        Assert.Equal("****9012", masked!.AccountNumber);

        using var sensitiveReader = factory.CreateClient();
        Auth(sensitiveReader,
            "payroll.payment_destination.view,payroll.payment_destination.view_sensitive");
        var sensitive = await sensitiveReader.GetFromJsonAsync<
            PayrollEmployeePaymentDestinationResponse>(
            $"{route}/effective?effectiveOn=2026-10-05");
        Assert.Equal("123456789012", sensitive!.AccountNumber);

        using var additionalOnly = factory.CreateClient();
        Auth(additionalOnly, "payroll.payment_destination.view", 303);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await additionalOnly.GetAsync($"{route}/effective?effectiveOn=2026-10-05")).StatusCode);

        using var foreignBusiness = factory.CreateClient();
        Auth(foreignBusiness, "payroll.payment_destination.view", 201, 2);
        Assert.Equal(HttpStatusCode.NotFound,
            (await foreignBusiness.GetAsync($"{route}/effective?effectiveOn=2026-10-05")).StatusCode);

        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.payment_destination.versioned";
        Assert.Equal(HttpStatusCode.InternalServerError, (await makerClient.PostAsJsonAsync(
            $"{route}/{created.Id}/versions",
            new VersionPayrollEmployeePaymentDestinationRequest(1,
                new PayrollEmployeePaymentDestinationData(
                    "Mobile", new DateOnly(2026, 10, 15), null, null, null,
                    "MobilePay", "Employee", "01712345678")))).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using (var versionRollbackScope = factory.Services.CreateScope())
        {
            var versionRollbackDb = versionRollbackScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Single(await versionRollbackDb.PayrollEmployeePaymentDestinations.ToArrayAsync());
        }
        using var version = await makerClient.PostAsJsonAsync(
            $"{route}/{created.Id}/versions",
            new VersionPayrollEmployeePaymentDestinationRequest(1,
                new PayrollEmployeePaymentDestinationData(
                    "Mobile", new DateOnly(2026, 10, 15), null, null, null,
                    "MobilePay", "Employee", "01712345678")));
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        var successor = (await version.Content.ReadFromJsonAsync<
            PayrollEmployeePaymentDestinationResponse>())!;
        var beforeSuccessor = await ordinaryReader.GetFromJsonAsync<
            PayrollEmployeePaymentDestinationResponse>(
            $"{route}/effective?effectiveOn=2026-10-14");
        var afterSuccessor = await ordinaryReader.GetFromJsonAsync<
            PayrollEmployeePaymentDestinationResponse>(
            $"{route}/effective?effectiveOn=2026-10-15");
        Assert.Equal("Bank", beforeSuccessor!.Method);
        Assert.Equal("Mobile", afterSuccessor!.Method);
        factory.RoleAuditFailureInterceptor.FailAuditActionType =
            "payroll.payment_destination.deactivated";
        Assert.Equal(HttpStatusCode.InternalServerError, (await makerClient.PostAsJsonAsync(
            $"{route}/{successor.Id}/deactivate",
            new DeactivatePayrollEmployeePaymentDestinationRequest(
                successor.Version, new DateOnly(2026, 11, 1)))).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;

        using var scopeAfter = factory.Services.CreateScope();
        var verify = scopeAfter.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(2, await verify.PayrollEmployeePaymentDestinations.CountAsync());
        Assert.Contains(await verify.AuditEvents.Where(row => row.BusinessId == 1).ToListAsync(),
            row => row.ActionType == "payroll.payment_destination.accessed");
        var auditFields = await verify.AuditEventFieldChanges.AsNoTracking()
            .Where(row => row.FieldName.Contains("AccountNumber"))
            .ToArrayAsync();
        Assert.All(auditFields, row => Assert.DoesNotContain("123456789012", row.NewValue));
    }

    [Fact]
    public void SalarySetupRoutesAppearInOpenApi()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();

        var paths = factory.Services.GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1").Paths.Keys;
        Assert.Contains("/api/payroll/v1/salary-components", paths);
        Assert.Contains("/api/payroll/v1/salary-components/{id}", paths);
        Assert.Contains("/api/payroll/v1/salary-structures", paths);
        Assert.Contains("/api/payroll/v1/salary-structures/{id}", paths);
    }

    [Fact]
    public async Task ComponentsAndStructuresAreAuthorizedBusinessScopedVersionedAndAudited()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory.Services);
        Auth(client, "");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/payroll/v1/salary-components")).StatusCode);
        SetPermissions(client, "payroll.salary.view,payroll.salary.manage");

        var baseResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/salary-components", Component("BASE", "Fixed"));
        Assert.Equal(HttpStatusCode.Created, baseResponse.StatusCode);
        Assert.NotNull(baseResponse.Headers.Location);
        var baseComponent = (await baseResponse.Content.ReadFromJsonAsync<PayrollSalaryComponentResponse>())!;

        var formulaResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/salary-components", Component("ALLOWANCE", "Formula", "BASE * 0.1"));
        Assert.Equal(HttpStatusCode.Created, formulaResponse.StatusCode);
        var formula = (await formulaResponse.Content.ReadFromJsonAsync<PayrollSalaryComponentResponse>())!;

        var invalidFormulaResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/salary-components", Component("INVALID", "Formula", "SUM(BASE)"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidFormulaResponse.StatusCode);
        var invalidCodeResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/salary-components", Component("BAD-CODE", "Fixed"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCodeResponse.StatusCode);
        var unknownReferenceResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/salary-components", Component("UNKNOWN_REF", "Formula", "MISSING + 1"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownReferenceResponse.StatusCode);
        var duplicateCodeResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/salary-components", Component("BASE", "Fixed"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateCodeResponse.StatusCode);

        var firstFormulaResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/salary-components", Component("FIRST", "Formula", "BASE + 1"));
        Assert.Equal(HttpStatusCode.Created, firstFormulaResponse.StatusCode);
        var firstFormula = (await firstFormulaResponse.Content.ReadFromJsonAsync<PayrollSalaryComponentResponse>())!;
        var secondFormulaResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/salary-components", Component("SECOND", "Formula", "FIRST + 1"));
        Assert.Equal(HttpStatusCode.Created, secondFormulaResponse.StatusCode);
        var cycleUpdateResponse = await client.PutAsJsonAsync(
            $"/api/payroll/v1/salary-components/{firstFormula.Id}",
            new PayrollSalaryComponentUpdateRequest(firstFormula.Version,
                firstFormula.Data with { FormulaExpression = "SECOND + 1" }));
        Assert.Equal(HttpStatusCode.BadRequest, cycleUpdateResponse.StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<PayrollSalaryComponentResponse>(
            $"/api/payroll/v1/salary-components/{firstFormula.Id}"))!.Version);

        var structureData = new PayrollSalaryStructureData("STANDARD", "Standard",
        [
            new(baseComponent.Id, 1000m, null, 1),
            new(formula.Id, null, null, 2)
        ]);
        var structureResponse = await client.PostAsJsonAsync("/api/payroll/v1/salary-structures", structureData);
        Assert.True(structureResponse.StatusCode == HttpStatusCode.Created,
            await structureResponse.Content.ReadAsStringAsync());
        var structure = (await structureResponse.Content.ReadFromJsonAsync<PayrollSalaryStructureResponse>())!;
        Assert.Equal(2, structure.Data.Lines.Count);

        var staleUpdate = await client.PutAsJsonAsync(
            $"/api/payroll/v1/salary-components/{baseComponent.Id}",
            new PayrollSalaryComponentUpdateRequest(baseComponent.Version,
                baseComponent.Data with { Name = "Updated" }));
        Assert.Equal(HttpStatusCode.OK, staleUpdate.StatusCode);
        var staleRetry = await client.PutAsJsonAsync(
            $"/api/payroll/v1/salary-components/{baseComponent.Id}",
            new PayrollSalaryComponentUpdateRequest(baseComponent.Version, baseComponent.Data));
        Assert.Equal(HttpStatusCode.Conflict, staleRetry.StatusCode);

        var inUseDeactivation = await client.PostAsJsonAsync(
            $"/api/payroll/v1/salary-components/{baseComponent.Id}/deactivate",
            new PayrollSalaryVersionRequest(2));
        Assert.Equal(HttpStatusCode.Conflict, inUseDeactivation.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        var foreignLookup = await client.GetAsync($"/api/payroll/v1/salary-components/{baseComponent.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignLookup.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(4, await db.Set<PayrollSalaryComponent>().CountAsync());
        Assert.Single(await db.Set<PayrollSalaryStructure>().ToListAsync());
        Assert.True(await db.Set<AuditEvent>().CountAsync() >= 6);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SalarySetupRequiresHrAndPayrollCapabilities(bool hrEnabled, bool payrollEnabled)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory.Services, hrEnabled, payrollEnabled);
        Auth(client, "payroll.salary.view,payroll.salary.manage");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/payroll/v1/salary-components")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(
                "/api/payroll/v1/salary-components", Component("BASE", "Fixed"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync(
                $"/api/payroll/v1/employees/1/adjustments?asOf={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}"))
            .StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/payroll/v1/adjustments",
                new SavePayrollEmployeeAdjustmentRequest(
                    null, null, 1, 1, "RecurringAllowance", "Manual", 10m,
                    DateOnly.FromDateTime(DateTime.UtcNow), null, "Capability check", null, "capability-key")))
            .StatusCode);
    }

    [Fact]
    public async Task EmployeeAdjustmentsAreScopedEffectiveDatedIdempotentAndVersioned()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        long earningId;
        long deductionId;
        long inactiveComponentId;
        long foreignComponentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await HrRosterPersistenceTests.Seed(db, businessExists: true);
            var employee = await db.HrEmployees.SingleAsync(row => row.BusinessId == 1 && row.Id == 1);
            employee.CurrentEmploymentStatus = "Active";
            var maker = await db.Users.SingleAsync(user => user.Id == 101);
            maker.OutletId = 10;
            maker.InventoryLocationAccessScope = User.InventoryAccessAssignedOnly;
            db.UserOutletAssignments.Add(new UserOutletAssignment
            {
                UserId = 101, BusinessId = 1, OutletId = 10, IsPrimary = true
            });
            db.Users.Add(new User
            {
                Id = 303, BusinessId = 1, Name = "Other location",
                Email = "other-location@example.invalid", PasswordHash = "test",
                IsActive = true, OutletId = 20,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly
            });
            db.Outlets.Add(new Outlet { Id = 20, BusinessId = 1, Name = "Other location" });
            db.OperationalLocationProjections.Add(new OperationalLocationProjection
            {
                Id = 21, BusinessId = 1, SourceSystem = "RetailPOS",
                SourceEntityType = "Outlet", SourceEntityId = "20",
                Name = "Other location", SourceVersion = 1
            });
            db.UserOutletAssignments.Add(new UserOutletAssignment
            {
                UserId = 303, BusinessId = 1, OutletId = 20, IsPrimary = true
            });
            var earning = new PayrollSalaryComponent
            {
                BusinessId = 1, Code = "RECURRING_ALLOWANCE", Name = "Transport",
                ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true
            };
            var deduction = new PayrollSalaryComponent
            {
                BusinessId = 1, Code = "ONE_TIME_DEDUCTION", Name = "Manual deduction",
                ComponentType = "Deduction", ValueType = "Fixed", IsRecurring = false
            };
            var inactive = new PayrollSalaryComponent
            {
                BusinessId = 1, Code = "INACTIVE", Name = "Inactive",
                ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true, IsActive = false
            };
            var foreign = new PayrollSalaryComponent
            {
                BusinessId = 2, Code = "FOREIGN", Name = "Foreign",
                ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true
            };
            db.PayrollSalaryComponents.AddRange(earning, deduction, inactive, foreign);
            await db.SaveChangesAsync();
            earningId = earning.Id;
            deductionId = deduction.Id;
            inactiveComponentId = inactive.Id;
            foreignComponentId = foreign.Id;
        }

        using var makerClient = factory.CreateClient();
        Auth(makerClient, "payroll.allowance_deduction.view,payroll.allowance_deduction.manage");
        var allowance = new SavePayrollEmployeeAdjustmentRequest(
            null, null, 1, earningId, "RecurringAllowance", "Manual",
            500m, today.AddDays(1), null, "Transport allowance", "POL-1", "allowance-create");
        using var noPermission = factory.CreateClient();
        Auth(noPermission, "payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPermission.PostAsJsonAsync("/api/payroll/v1/adjustments", allowance)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPermission.GetAsync(
                $"/api/payroll/v1/employees/1/adjustments?asOf={today:yyyy-MM-dd}")).StatusCode);

        using var createdResponse = await makerClient.PostAsJsonAsync(
            "/api/payroll/v1/adjustments", allowance);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content
            .ReadFromJsonAsync<PayrollEmployeeAdjustmentResponse>())!;
        Assert.Equal("Active", created.Status);
        Assert.Equal(1, created.Version);
        Assert.Empty((await makerClient.GetFromJsonAsync<IReadOnlyList<PayrollEmployeeAdjustmentResponse>>(
            $"/api/payroll/v1/employees/1/adjustments?asOf={today:yyyy-MM-dd}")) ?? []);
        var replay = await makerClient.PostAsJsonAsync("/api/payroll/v1/adjustments", allowance);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<PayrollEmployeeAdjustmentResponse>())!.IsReplay);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var component = await db.PayrollSalaryComponents.SingleAsync(row => row.Id == earningId);
            component.IsActive = false;
            await db.SaveChangesAsync();
            var stateIndependentReplay = await makerClient.PostAsJsonAsync(
                "/api/payroll/v1/adjustments", allowance);
            Assert.Equal(HttpStatusCode.OK, stateIndependentReplay.StatusCode);
            Assert.True((await stateIndependentReplay.Content
                .ReadFromJsonAsync<PayrollEmployeeAdjustmentResponse>())!.IsReplay);
            component.IsActive = true;
            await db.SaveChangesAsync();
        }
        var conflict = await makerClient.PostAsJsonAsync("/api/payroll/v1/adjustments",
            allowance with { Amount = 600m });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var deductionBody = allowance with
        {
            SalaryComponentId = deductionId,
            AdjustmentKind = "OneTimeDeduction",
            Amount = 125m,
            EffectiveFrom = today,
            Reason = "Uniform deduction",
            Reference = null,
            IdempotencyKey = "deduction-create"
        };
        using var deductionResponse = await makerClient.PostAsJsonAsync(
            "/api/payroll/v1/adjustments", deductionBody);
        Assert.Equal(HttpStatusCode.Created, deductionResponse.StatusCode);
        var oneTime = (await deductionResponse.Content
            .ReadFromJsonAsync<PayrollEmployeeAdjustmentResponse>())!;
        Assert.Equal(125m, oneTime.Amount);
        var wrongKind = await makerClient.PostAsJsonAsync("/api/payroll/v1/adjustments",
            deductionBody with { AdjustmentKind = "RecurringDeduction", IdempotencyKey = "wrong-kind" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);
        var inactiveComponent = await makerClient.PostAsJsonAsync("/api/payroll/v1/adjustments",
            allowance with { SalaryComponentId = inactiveComponentId, IdempotencyKey = "inactive-component" });
        Assert.Equal(HttpStatusCode.NotFound, inactiveComponent.StatusCode);
        var foreignComponent = await makerClient.PostAsJsonAsync("/api/payroll/v1/adjustments",
            allowance with { SalaryComponentId = foreignComponentId, IdempotencyKey = "foreign-component" });
        Assert.Equal(HttpStatusCode.NotFound, foreignComponent.StatusCode);
        var componentMismatch = await makerClient.PostAsJsonAsync("/api/payroll/v1/adjustments",
            allowance with { AdjustmentKind = "OneTimeEarning", IdempotencyKey = "component-mismatch" });
        Assert.Equal(HttpStatusCode.BadRequest, componentMismatch.StatusCode);

        var nextDate = today.AddDays(5);
        var successorBody = allowance with
        {
            AdjustmentIdentity = created.AdjustmentIdentity,
            ExpectedVersion = created.Version,
            Amount = 750m,
            EffectiveFrom = nextDate,
            IdempotencyKey = "allowance-version-2"
        };
        using var successorResponse = await makerClient.PutAsJsonAsync(
            $"/api/payroll/v1/adjustments/{created.AdjustmentIdentity}", successorBody);
        Assert.Equal(HttpStatusCode.OK, successorResponse.StatusCode);
        var successor = (await successorResponse.Content
            .ReadFromJsonAsync<PayrollEmployeeAdjustmentResponse>())!;
        Assert.Equal(2, successor.Version);
        Assert.Equal(created.Id, successor.PredecessorVersionId);
        Assert.Equal(500m, (await makerClient.GetFromJsonAsync<
            IReadOnlyList<PayrollEmployeeAdjustmentResponse>>(
                $"/api/payroll/v1/employees/1/adjustments?asOf={today.AddDays(2):yyyy-MM-dd}"))!
            .Single(row => row.AdjustmentIdentity == created.AdjustmentIdentity).Amount);
        Assert.Equal(750m, (await makerClient.GetFromJsonAsync<
            IReadOnlyList<PayrollEmployeeAdjustmentResponse>>(
                $"/api/payroll/v1/employees/1/adjustments?asOf={nextDate:yyyy-MM-dd}"))!
            .Single(row => row.AdjustmentIdentity == created.AdjustmentIdentity).Amount);

        using var unauthorizedLocation = factory.CreateClient();
        Auth(unauthorizedLocation, "payroll.allowance_deduction.view", 303);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await unauthorizedLocation.GetAsync(
                $"/api/payroll/v1/employees/1/adjustments?asOf={nextDate:yyyy-MM-dd}")).StatusCode);
        using var crossBusiness = factory.CreateClient();
        Auth(crossBusiness, "payroll.allowance_deduction.view", 201, 2);
        Assert.Equal(HttpStatusCode.NotFound,
            (await crossBusiness.GetAsync(
                $"/api/payroll/v1/employees/1/adjustments?asOf={nextDate:yyyy-MM-dd}")).StatusCode);
        using var crossBusinessHistory = await crossBusiness.GetAsync(
            $"/api/payroll/v1/adjustments/{created.AdjustmentIdentity}/history");
        Assert.Equal(HttpStatusCode.OK, crossBusinessHistory.StatusCode);
        Assert.Empty((await crossBusinessHistory.Content
            .ReadFromJsonAsync<IReadOnlyList<PayrollEmployeeAdjustmentResponse>>()) ?? []);

        using var historyResponse = await makerClient.GetAsync(
            $"/api/payroll/v1/adjustments/{created.AdjustmentIdentity}/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = (await historyResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<PayrollEmployeeAdjustmentResponse>>())!;
        Assert.Equal(2, history.Count);
        Assert.Contains(history, version => version.Amount == 500m);
        Assert.Contains(history, version => version.Amount == 750m);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var accessAudits = await db.AuditEvents.Where(row =>
                row.ActionType == "payroll.allowance_deduction.accessed"
                || row.ActionType == "payroll.allowance_deduction.history_accessed").ToListAsync();
            Assert.NotEmpty(accessAudits);
            Assert.All(accessAudits, row => Assert.DoesNotContain("500", row.ActionSummary));
        }

        var ending = new EndPayrollEmployeeAdjustmentRequest(
            created.AdjustmentIdentity, successor.Version, today.AddDays(10), "Allowance ended", "allowance-end");
        using var endedResponse = await makerClient.PostAsJsonAsync(
            $"/api/payroll/v1/adjustments/{created.AdjustmentIdentity}/end", ending);
        Assert.Equal(HttpStatusCode.OK, endedResponse.StatusCode);
        var ended = (await endedResponse.Content.ReadFromJsonAsync<PayrollEmployeeAdjustmentResponse>())!;
        Assert.Equal(3, ended.Version);
        Assert.Equal("Inactive", ended.Status);
        Assert.Equal(750m, (await makerClient.GetFromJsonAsync<
            IReadOnlyList<PayrollEmployeeAdjustmentResponse>>(
                $"/api/payroll/v1/employees/1/adjustments?asOf={today.AddDays(9):yyyy-MM-dd}"))!
            .Single(row => row.AdjustmentIdentity == created.AdjustmentIdentity).Amount);
        Assert.DoesNotContain((await makerClient.GetFromJsonAsync<
            IReadOnlyList<PayrollEmployeeAdjustmentResponse>>(
                $"/api/payroll/v1/employees/1/adjustments?asOf={today.AddDays(10):yyyy-MM-dd}")) ?? [],
            row => row.AdjustmentIdentity == created.AdjustmentIdentity);
        var endReplay = await makerClient.PostAsJsonAsync(
            $"/api/payroll/v1/adjustments/{created.AdjustmentIdentity}/end", ending);
        Assert.Equal(HttpStatusCode.OK, endReplay.StatusCode);
        Assert.True((await endReplay.Content.ReadFromJsonAsync<PayrollEmployeeAdjustmentResponse>())!.IsReplay);

        using (var scope = factory.Services.CreateScope())
        {
            var beforeFailedAudit = await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
                .PayrollEmployeeAdjustmentVersions.CountAsync();
            factory.RoleAuditFailureInterceptor.FailPayrollAdjustmentAudit = true;
            var failedAuditResponse = await makerClient.PostAsJsonAsync("/api/payroll/v1/adjustments",
                allowance with { IdempotencyKey = "audit-failure" });
            Assert.Equal(HttpStatusCode.InternalServerError, failedAuditResponse.StatusCode);
            factory.RoleAuditFailureInterceptor.FailPayrollAdjustmentAudit = false;
            Assert.Equal(beforeFailedAudit,
                await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
                    .PayrollEmployeeAdjustmentVersions.CountAsync());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var version = await db.PayrollEmployeeAdjustmentVersions
                .SingleAsync(row => row.Id == created.Id);
            version.Amount = 1m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.Entry(version).State = EntityState.Unchanged;
            db.Remove(version);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task InitialSalarySetupUsesConfiguredMakerCheckerAndIdentityLocationScope()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await Seed(factory.Services);
        long structureId;
        long componentId;
        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await HrRosterPersistenceTests.Seed(db, businessExists: true);
            var makerUser = await db.Users.SingleAsync(user => user.Id == 101);
            makerUser.OutletId = 10;
            makerUser.InventoryLocationAccessScope = User.InventoryAccessAssignedOnly;
            var employee = await db.HrEmployees.SingleAsync(row => row.BusinessId == 1 && row.Id == 1);
            employee.CurrentEmploymentStatus = "Active";
            var cancelEmployee = new HrEmployee
            {
                BusinessId = 1, EmployeeCode = "EMP-CANCEL", FirstName = "Cancel",
                DisplayName = "Cancel Employee", CurrentEmploymentTypeId = 1,
                CurrentEmploymentStatus = "Active", JoiningDate = new DateOnly(2026, 1, 1),
                CurrentPrimaryLocationProjectionId = 10
            };
            var rejectEmployee = new HrEmployee
            {
                BusinessId = 1, EmployeeCode = "EMP-REJECT", FirstName = "Reject",
                DisplayName = "Reject Employee", CurrentEmploymentTypeId = 1,
                CurrentEmploymentStatus = "Active", JoiningDate = new DateOnly(2026, 1, 1),
                CurrentPrimaryLocationProjectionId = 10
            };
            db.HrEmployees.AddRange(cancelEmployee, rejectEmployee);
            db.Outlets.AddRange(
                new Outlet { Id = 20, BusinessId = 1, Name = "Additional location" },
                new Outlet { Id = 30, BusinessId = 2, Name = "Foreign location" });
            db.OperationalLocationProjections.AddRange(
                new OperationalLocationProjection
                {
                    Id = 21, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
                    SourceEntityId = "20", Name = "Additional location", SourceVersion = 1
                },
                new OperationalLocationProjection
                {
                    Id = 30, BusinessId = 2, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
                    SourceEntityId = "30", Name = "Foreign location", SourceVersion = 1
                });
            db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
            {
                BusinessId = 1, EmployeeId = 1, OperationalLocationProjectionId = 21,
                AssignmentType = "Additional", EffectiveFrom = new DateOnly(2026, 1, 1),
                LocationNameSnapshot = "Additional location", SourceSystemSnapshot = "RetailPOS",
                SourceEntityTypeSnapshot = "Outlet", SourceEntityIdSnapshot = "20",
                LocationSourceVersionSnapshot = 1, RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow
            });
            db.Users.AddRange(
                new User
                {
                    Id = 202, BusinessId = 1, Name = "Salary approver",
                    Email = "salary-approver@example.invalid", PasswordHash = "test", IsActive = true,
                    InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, OutletId = 10
                },
                new User
                {
                    Id = 303, BusinessId = 1, Name = "Additional-location user",
                    Email = "additional-location@example.invalid", PasswordHash = "test", IsActive = true,
                    InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, OutletId = 20
                },
                new User
                {
                    Id = 201, BusinessId = 2, Name = "Foreign payroll user",
                    Email = "foreign-payroll@example.invalid", PasswordHash = "test", IsActive = true,
                    InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, OutletId = 30
                });
            db.UserOutletAssignments.AddRange(
                new UserOutletAssignment { UserId = 101, BusinessId = 1, OutletId = 10, IsPrimary = true },
                new UserOutletAssignment { UserId = 202, BusinessId = 1, OutletId = 10, IsPrimary = true },
                new UserOutletAssignment { UserId = 303, BusinessId = 1, OutletId = 20, IsPrimary = true },
                new UserOutletAssignment { UserId = 201, BusinessId = 2, OutletId = 30, IsPrimary = true });
            await db.SaveChangesAsync();
            db.HrEmployeeLocationAssignments.AddRange(
                PrimaryLocation(cancelEmployee.Id),
                PrimaryLocation(rejectEmployee.Id));
            db.HrEmploymentPeriods.AddRange(
                new HrEmploymentPeriod
                {
                    BusinessId = 1, EmployeeId = cancelEmployee.Id, EmploymentTypeId = 1,
                    StartDate = new DateOnly(2026, 1, 1)
                },
                new HrEmploymentPeriod
                {
                    BusinessId = 1, EmployeeId = rejectEmployee.Id, EmploymentTypeId = 1,
                    StartDate = new DateOnly(2026, 1, 1)
                });

            var component = new PayrollSalaryComponent
            {
                BusinessId = 1, Code = "BASE", Name = "Base salary",
                ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true,
                CalculationOrder = 1
            };
            db.PayrollSalaryComponents.Add(component);
            await db.SaveChangesAsync();
            componentId = component.Id;
            var structure = new PayrollSalaryStructure
            {
                BusinessId = 1,
                Code = "STANDARD",
                Name = "Standard",
                Lines =
                [
                    new PayrollSalaryStructureLine
                    {
                        BusinessId = 1, SalaryComponentId = component.Id,
                        DefaultValue = 1000m, CalculationOrder = 1
                    }
                ]
            };
            db.PayrollSalaryStructures.Add(structure);
            var policy = new ApprovalPolicy
            {
                BusinessId = 1,
                Code = "SALARY_SETUP_API_TEST",
                ProcessType = ApprovalProcessType.SalarySetup,
                Name = "Deterministic salary setup policy",
                VersionNo = 1,
                ScopeType = ApprovalScopeType.Location,
                ScopeReference = "10",
                EffectiveFrom = effectiveFrom,
                IsActive = true,
                Steps =
                [
                    new ApprovalPolicyStep
                    {
                        Sequence = 1, ApproverType = ApprovalApproverType.User,
                        ApproverReference = "202", RequiredApprovals = 1,
                        CanReject = true, CanCancel = true
                    }
                ]
            };
            db.ApprovalPolicies.Add(policy);
            db.ApprovalPolicies.Add(new ApprovalPolicy
            {
                BusinessId = 1,
                Code = "SALARY_REVISION_API_TEST",
                ProcessType = ApprovalProcessType.SalaryRevision,
                Name = "Deterministic salary revision policy",
                VersionNo = 1,
                ScopeType = ApprovalScopeType.Location,
                ScopeReference = "10",
                EffectiveFrom = effectiveFrom,
                IsActive = true,
                Steps =
                [
                    new ApprovalPolicyStep
                    {
                        Sequence = 1, ApproverType = ApprovalApproverType.User,
                        ApproverReference = "202", RequiredApprovals = 1,
                        CanReject = true, CanCancel = true
                    }
                ]
            });
            await db.SaveChangesAsync();
            structureId = structure.Id;
            componentId = component.Id;
        }

        using var maker = factory.CreateClient();
        Auth(maker, "payroll.salary.manage,payroll.salary.view");
        var requestBody = new CreatePayrollSalarySetupRequest(
            1, structureId, effectiveFrom,
            [new PayrollSalarySetupComponentInput(componentId, 1250m)], "Initial hire", "salary-api-once");
        using var missingManage = factory.CreateClient();
        Auth(missingManage, "payroll.salary.view,payroll.salary_setup.create");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingManage.PostAsJsonAsync(
                "/api/payroll/v1/salary-setup-requests", requestBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await maker.PostAsJsonAsync("/api/payroll/v1/salary-setup-requests", requestBody)).StatusCode);
        SetPermissions(maker,
            "payroll.salary.manage,payroll.salary.view,payroll.salary_setup.create");

        using var unauthorizedLocation = factory.CreateClient();
        Auth(unauthorizedLocation,
            "payroll.salary.manage,payroll.salary.view,payroll.salary_setup.create", 303);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await unauthorizedLocation.PostAsJsonAsync(
                "/api/payroll/v1/salary-setup-requests", requestBody)).StatusCode);

        using var createdResponse = await maker.PostAsJsonAsync(
            "/api/payroll/v1/salary-setup-requests", requestBody);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var request = (await createdResponse.Content.ReadFromJsonAsync<PayrollSalarySetupRequestResponse>())!;
        Assert.Equal("Draft", request.Status);
        using var detailResponse = await maker.GetAsync(
            $"/api/payroll/v1/salary-setup-requests/{request.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        using var noViewPermission = factory.CreateClient();
        Auth(noViewPermission, "payroll.salary_setup.submit");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noViewPermission.GetAsync(
                $"/api/payroll/v1/salary-setup-requests/{request.Id}")).StatusCode);

        using var foreignBusiness = factory.CreateClient();
        Auth(foreignBusiness, "payroll.salary.view", 201, 2);
        Assert.Equal(HttpStatusCode.NotFound,
            (await foreignBusiness.GetAsync(
                $"/api/payroll/v1/salary-setup-requests/{request.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await maker.GetAsync(
                $"/api/payroll/v1/employees/1/salary?asOf={effectiveFrom:yyyy-MM-dd}")).StatusCode);

        SetPermissions(maker, "payroll.salary_setup.submit,payroll.salary.view");
        using var submittedResponse = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/salary-setup-requests/{request.Id}/submit",
            new PayrollSalarySetupVersionRequest(request.Version));
        Assert.Equal(HttpStatusCode.OK, submittedResponse.StatusCode);
        using var submittedJson = System.Text.Json.JsonDocument.Parse(
            await submittedResponse.Content.ReadAsStringAsync());
        var approvalId = submittedJson.RootElement.GetProperty("instanceId").GetInt64();
        Assert.True(approvalId > 0);

        using var selfApproval = factory.CreateClient();
        Auth(selfApproval, "payroll.salary_setup.approve");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await selfApproval.PostAsJsonAsync(
                $"/api/payroll/v1/salary-setup-approvals/{approvalId}/approve",
                new PayrollSalarySetupDecisionRequest("self-approve", "Not allowed"))).StatusCode);

        using var approver = factory.CreateClient();
        Auth(approver, "payroll.salary_setup.approve", 202);
        using var missingApprovalPermission = factory.CreateClient();
        Auth(missingApprovalPermission, "payroll.salary.view", 202);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingApprovalPermission.PostAsJsonAsync(
                $"/api/payroll/v1/salary-setup-approvals/{approvalId}/approve",
                new PayrollSalarySetupDecisionRequest("missing-approve", "Not allowed"))).StatusCode);
        using var approvedResponse = await approver.PostAsJsonAsync(
            $"/api/payroll/v1/salary-setup-approvals/{approvalId}/approve",
            new PayrollSalarySetupDecisionRequest("salary-approved", "Approved"));
        Assert.True(approvedResponse.StatusCode == HttpStatusCode.OK,
            await approvedResponse.Content.ReadAsStringAsync());

        SetPermissions(maker, "payroll.salary.view");
        using var salaryResponse = await maker.GetAsync(
            $"/api/payroll/v1/employees/1/salary?asOf={effectiveFrom:yyyy-MM-dd}");
        Assert.True(salaryResponse.StatusCode == HttpStatusCode.OK,
            await salaryResponse.Content.ReadAsStringAsync());
        var salary = (await salaryResponse.Content.ReadFromJsonAsync<PayrollEmployeeSalaryResponse>())!;
        Assert.Equal(1250m, Assert.Single(salary.Components).Value);
        Assert.Equal(approvalId, salary.ApprovalInstanceId);

        var revisionEffectiveFrom = effectiveFrom.AddDays(1);
        var revisionBody = new CreatePayrollSalaryRevisionRequest(
            1, salary.Id, revisionEffectiveFrom,
            [new PayrollSalarySetupComponentInput(componentId, 1500m)],
            "Annual adjustment", "salary-revision-api-once");
        SetPermissions(maker, "payroll.salary.view,payroll.salary.manage,payroll.salary_revision.create");
        using var missingRevisionPermission = factory.CreateClient();
        Auth(missingRevisionPermission, "payroll.salary.view,payroll.salary.manage");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingRevisionPermission.PostAsJsonAsync(
                "/api/payroll/v1/salary-revision-requests", revisionBody)).StatusCode);
        using var revisionCreatedResponse = await maker.PostAsJsonAsync(
            "/api/payroll/v1/salary-revision-requests", revisionBody);
        Assert.Equal(HttpStatusCode.Created, revisionCreatedResponse.StatusCode);
        var revision = (await revisionCreatedResponse.Content
            .ReadFromJsonAsync<PayrollSalaryRevisionRequestResponse>())!;
        Assert.Equal("Draft", revision.Status);
        using var revisionDetail = await maker.GetAsync(
            $"/api/payroll/v1/salary-revision-requests/{revision.Id}");
        Assert.Equal(HttpStatusCode.OK, revisionDetail.StatusCode);
        using var additionalLocationUser = factory.CreateClient();
        Auth(additionalLocationUser, "payroll.salary.view", 303);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await additionalLocationUser.GetAsync(
                $"/api/payroll/v1/salary-revision-requests/{revision.Id}")).StatusCode);
        using var foreignRevisionUser = factory.CreateClient();
        Auth(foreignRevisionUser, "payroll.salary.view", 201, 2);
        Assert.Equal(HttpStatusCode.NotFound,
            (await foreignRevisionUser.GetAsync(
                $"/api/payroll/v1/salary-revision-requests/{revision.Id}")).StatusCode);
        SetPermissions(maker, "payroll.salary.view");
        var salaryBeforeRevision = await maker.GetFromJsonAsync<PayrollEmployeeSalaryResponse>(
            $"/api/payroll/v1/employees/1/salary?asOf={revisionEffectiveFrom:yyyy-MM-dd}");
        Assert.Equal(salary.Id, salaryBeforeRevision!.Id);

        SetPermissions(maker, "payroll.salary.view,payroll.salary_revision.submit");
        using var revisionSubmitResponse = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/salary-revision-requests/{revision.Id}/submit",
            new PayrollSalarySetupVersionRequest(revision.Version));
        Assert.Equal(HttpStatusCode.OK, revisionSubmitResponse.StatusCode);
        using var revisionSubmitJson = System.Text.Json.JsonDocument.Parse(
            await revisionSubmitResponse.Content.ReadAsStringAsync());
        var revisionApprovalId = revisionSubmitJson.RootElement.GetProperty("instanceId").GetInt64();
        using var revisionSelfApproval = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/salary-revision-approvals/{revisionApprovalId}/approve",
            new PayrollSalarySetupDecisionRequest("revision-self-approval", "Not allowed"));
        Assert.Equal(HttpStatusCode.Forbidden, revisionSelfApproval.StatusCode);
        using var revisionApprover = factory.CreateClient();
        Auth(revisionApprover, "payroll.salary_revision.approve", 202);
        using var revisionApproved = await revisionApprover.PostAsJsonAsync(
            $"/api/payroll/v1/salary-revision-approvals/{revisionApprovalId}/approve",
            new PayrollSalarySetupDecisionRequest("revision-approved", "Approved"));
        Assert.True(revisionApproved.StatusCode == HttpStatusCode.OK,
            await revisionApproved.Content.ReadAsStringAsync());
        SetPermissions(maker, "payroll.salary.view");
        using var salaryAfterRevisionResponse = await maker.GetAsync(
            $"/api/payroll/v1/employees/1/salary?asOf={revisionEffectiveFrom:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, salaryAfterRevisionResponse.StatusCode);
        var salaryAfterRevision = (await salaryAfterRevisionResponse.Content
            .ReadFromJsonAsync<PayrollEmployeeSalaryResponse>())!;
        Assert.NotEqual(salary.Id, salaryAfterRevision.Id);
        Assert.Equal(1500m, Assert.Single(salaryAfterRevision.Components).Value);

        SetPermissions(maker, "payroll.salary.view,payroll.salary.manage,payroll.salary_revision.create");
        var rejectedRevisionBody = revisionBody with
        {
            SourceSalaryVersionId = salaryAfterRevision.Id,
            EffectiveFrom = revisionEffectiveFrom.AddDays(1),
            IdempotencyKey = "salary-revision-api-reject"
        };
        using var rejectedRevisionCreate = await maker.PostAsJsonAsync(
            "/api/payroll/v1/salary-revision-requests", rejectedRevisionBody);
        Assert.Equal(HttpStatusCode.Created, rejectedRevisionCreate.StatusCode);
        var rejectedRevision = (await rejectedRevisionCreate.Content
            .ReadFromJsonAsync<PayrollSalaryRevisionRequestResponse>())!;
        SetPermissions(maker, "payroll.salary.view,payroll.salary_revision.submit");
        using var rejectedRevisionSubmit = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/salary-revision-requests/{rejectedRevision.Id}/submit",
            new PayrollSalarySetupVersionRequest(rejectedRevision.Version));
        Assert.Equal(HttpStatusCode.OK, rejectedRevisionSubmit.StatusCode);
        using var rejectedRevisionJson = System.Text.Json.JsonDocument.Parse(
            await rejectedRevisionSubmit.Content.ReadAsStringAsync());
        var rejectedRevisionApprovalId =
            rejectedRevisionJson.RootElement.GetProperty("instanceId").GetInt64();
        using var rejectedRevisionDecision = await revisionApprover.PostAsJsonAsync(
            $"/api/payroll/v1/salary-revision-approvals/{rejectedRevisionApprovalId}/reject",
            new PayrollSalarySetupDecisionRequest("revision-rejected", "Revision rejected"));
        Assert.Equal(HttpStatusCode.OK, rejectedRevisionDecision.StatusCode);
        using var salaryAfterRejectionResponse = await maker.GetAsync(
            $"/api/payroll/v1/employees/1/salary?asOf={rejectedRevisionBody.EffectiveFrom:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, salaryAfterRejectionResponse.StatusCode);
        Assert.Equal(salaryAfterRevision.Id,
            (await salaryAfterRejectionResponse.Content.ReadFromJsonAsync<PayrollEmployeeSalaryResponse>())!.Id);

        SetPermissions(maker, "payroll.salary.view,payroll.salary.manage,payroll.salary_revision.create");
        var cancelledRevisionBody = rejectedRevisionBody with
        {
            EffectiveFrom = revisionEffectiveFrom.AddDays(2),
            IdempotencyKey = "salary-revision-api-cancel"
        };
        using var cancelledRevisionCreate = await maker.PostAsJsonAsync(
            "/api/payroll/v1/salary-revision-requests", cancelledRevisionBody);
        Assert.Equal(HttpStatusCode.Created, cancelledRevisionCreate.StatusCode);
        var cancelledRevision = (await cancelledRevisionCreate.Content
            .ReadFromJsonAsync<PayrollSalaryRevisionRequestResponse>())!;
        SetPermissions(maker, "payroll.salary.view,payroll.salary_revision.submit");
        using var cancelledRevisionSubmit = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/salary-revision-requests/{cancelledRevision.Id}/submit",
            new PayrollSalarySetupVersionRequest(cancelledRevision.Version));
        Assert.Equal(HttpStatusCode.OK, cancelledRevisionSubmit.StatusCode);
        using var cancelledRevisionJson = System.Text.Json.JsonDocument.Parse(
            await cancelledRevisionSubmit.Content.ReadAsStringAsync());
        var cancelledRevisionApprovalId =
            cancelledRevisionJson.RootElement.GetProperty("instanceId").GetInt64();
        using var cancelledRevisionDecision = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/salary-revision-approvals/{cancelledRevisionApprovalId}/cancel",
            new PayrollSalarySetupDecisionRequest("revision-cancelled", "Revision withdrawn"));
        Assert.Equal(HttpStatusCode.OK, cancelledRevisionDecision.StatusCode);
        using var salaryAfterCancellationResponse = await maker.GetAsync(
            $"/api/payroll/v1/employees/1/salary?asOf={cancelledRevisionBody.EffectiveFrom:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, salaryAfterCancellationResponse.StatusCode);
        Assert.Equal(salaryAfterRevision.Id,
            (await salaryAfterCancellationResponse.Content
                .ReadFromJsonAsync<PayrollEmployeeSalaryResponse>())!.Id);
        var revisionHistory = await maker.GetFromJsonAsync<IReadOnlyList<PayrollSalaryRevisionRequestResponse>>(
            "/api/payroll/v1/employees/1/salary-revision-requests");
        Assert.Equal(
            ["Approved", "Rejected", "Cancelled"],
            revisionHistory!.OrderBy(row => row.Id).Select(row => row.Status));

        SetPermissions(maker,
            "payroll.salary.manage,payroll.salary.view,payroll.salary_setup.create,payroll.salary_setup.submit");
        var cancelBody = requestBody with
        {
            EmployeeId = 2,
            Reason = "Cancelled hire",
            IdempotencyKey = "salary-api-cancel"
        };
        using var cancelCreate = await maker.PostAsJsonAsync(
            "/api/payroll/v1/salary-setup-requests", cancelBody);
        Assert.True(cancelCreate.StatusCode == HttpStatusCode.Created,
            await cancelCreate.Content.ReadAsStringAsync());
        var cancelRequest = (await cancelCreate.Content
            .ReadFromJsonAsync<PayrollSalarySetupRequestResponse>())!;
        using var cancelSubmit = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/salary-setup-requests/{cancelRequest.Id}/submit",
            new PayrollSalarySetupVersionRequest(cancelRequest.Version));
        Assert.Equal(HttpStatusCode.OK, cancelSubmit.StatusCode);
        using var cancelJson = System.Text.Json.JsonDocument.Parse(
            await cancelSubmit.Content.ReadAsStringAsync());
        var cancelApprovalId = cancelJson.RootElement.GetProperty("instanceId").GetInt64();
        using var cancelled = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/salary-setup-approvals/{cancelApprovalId}/cancel",
            new PayrollSalarySetupDecisionRequest("salary-cancelled", "Request withdrawn"));
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await maker.GetAsync(
                $"/api/payroll/v1/employees/2/salary?asOf={effectiveFrom:yyyy-MM-dd}")).StatusCode);

        var rejectBody = requestBody with
        {
            EmployeeId = 3,
            Reason = "Rejected hire",
            IdempotencyKey = "salary-api-reject"
        };
        using var rejectCreate = await maker.PostAsJsonAsync(
            "/api/payroll/v1/salary-setup-requests", rejectBody);
        Assert.Equal(HttpStatusCode.Created, rejectCreate.StatusCode);
        var rejectRequest = (await rejectCreate.Content
            .ReadFromJsonAsync<PayrollSalarySetupRequestResponse>())!;
        using var rejectSubmit = await maker.PostAsJsonAsync(
            $"/api/payroll/v1/salary-setup-requests/{rejectRequest.Id}/submit",
            new PayrollSalarySetupVersionRequest(rejectRequest.Version));
        Assert.Equal(HttpStatusCode.OK, rejectSubmit.StatusCode);
        using var rejectJson = System.Text.Json.JsonDocument.Parse(
            await rejectSubmit.Content.ReadAsStringAsync());
        var rejectApprovalId = rejectJson.RootElement.GetProperty("instanceId").GetInt64();
        using var rejected = await approver.PostAsJsonAsync(
            $"/api/payroll/v1/salary-setup-approvals/{rejectApprovalId}/reject",
            new PayrollSalarySetupDecisionRequest("salary-rejected", "Insufficient evidence"));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await maker.GetAsync(
                $"/api/payroll/v1/employees/3/salary?asOf={effectiveFrom:yyyy-MM-dd}")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(2, await db.PayrollEmployeeSalaryVersions
                .CountAsync(row => row.BusinessId == 1 && row.EmployeeId == 1));
            Assert.Contains(await db.AuditEvents.Where(row => row.BusinessId == 1).ToListAsync(),
                row => row.ActionType == "payroll.employee_salary.accessed");
        }
    }

    private static PayrollSalaryComponentData Component(
        string code, string valueType, string? formula = null) =>
        new(code, code, "Earning", valueType, null, true, 1, formula);

    private static HrEmployeeLocationAssignment PrimaryLocation(long employeeId) => new()
    {
        BusinessId = 1, EmployeeId = employeeId, OperationalLocationProjectionId = 10,
        AssignmentType = "Primary", EffectiveFrom = new DateOnly(2026, 1, 1),
        LocationNameSnapshot = "Outlet", SourceSystemSnapshot = "RetailPOS",
        SourceEntityTypeSnapshot = "Outlet", SourceEntityIdSnapshot = "10",
        LocationSourceVersionSnapshot = 1, RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow
    };

    private static async Task Seed(
        IServiceProvider services, bool hrEnabled = true, bool payrollEnabled = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "Payroll" },
            new Business { Id = 2, Name = "Other Payroll" });
        foreach (var businessId in new long[] { 1, 2 })
        {
            db.BusinessFeatureSettings.AddRange(
                new BusinessFeatureSetting
                {
                    BusinessId = businessId,
                    FeatureKey = "hr.enabled",
                    IsEnabled = businessId == 1 ? hrEnabled : true
                },
                new BusinessFeatureSetting
                {
                    BusinessId = businessId,
                    FeatureKey = "payroll.enabled",
                    IsEnabled = businessId == 1 ? payrollEnabled : true
                });
        }
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Payroll actor",
            Email = "payroll@example.invalid",
            PasswordHash = "test",
            IsActive = true,
            InventoryLocationAccessScope = User.InventoryAccessAll
        });
        await db.SaveChangesAsync();
    }

    private static void Auth(HttpClient client, string permissions, long userId = 101, long businessId = 1)
    {
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "HR");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }

    private static void SetPermissions(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }
}
