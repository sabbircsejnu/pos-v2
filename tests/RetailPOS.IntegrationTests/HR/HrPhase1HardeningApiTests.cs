using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Controllers.HR;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrPhase1HardeningApiTests
{
    [Fact]
    public async Task StartedApiDescribesImplementedHrRoutesAndRegistersEveryActionPolicy()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var requiredRoutes = new Dictionary<string, string[]>
        {
            ["/api/hr/v1/capabilities"] = ["get"],
            ["/api/hr/v1/org-units"] = ["get", "post"],
            ["/api/hr/v1/org-units/{id}"] = ["put"],
            ["/api/hr/v1/operational-locations"] = ["get"],
            ["/api/hr/v1/operational-locations/{id}"] = ["get"],
            ["/api/hr/v1/operational-locations/reconcile"] = ["post"],
            ["/api/hr/v1/positions"] = ["get", "post"],
            ["/api/hr/v1/positions/{id}"] = ["put"],
            ["/api/hr/v1/positions/{id}/deactivate"] = ["post"],
            ["/api/hr/v1/employment-types"] = ["get"],
            ["/api/hr/v1/me/employee"] = ["get"],
            ["/api/hr/v1/employees"] = ["get", "post"],
            ["/api/hr/v1/employees/{id}"] = ["get", "put"],
            ["/api/hr/v1/employees/{id}/lifecycle"] = ["get", "post"],
            ["/api/hr/v1/employees/{id}/rehire"] = ["post"],
            ["/api/hr/v1/employees/{id}/positions"] = ["get", "post"],
            ["/api/hr/v1/employees/{id}/locations"] = ["get"],
            ["/api/hr/v1/employees/{id}/user-link"] = ["get", "post", "delete"],
            ["/api/hr/v1/employees/{id}/location-transfers"] = ["get", "post"],
            ["/api/hr/v1/employees/{id}/location-assignments"] = ["post"],
            ["/api/hr/v1/employees/{id}/location-transfers/{transferId}/approve"] = ["post"],
            ["/api/hr/v1/employees/{id}/location-transfers/{transferId}/reject"] = ["post"],
            ["/api/hr/v1/employees/{id}/location-transfers/{transferId}/cancel"] = ["post"],
            ["/api/hr/v1/employee-document-types"] = ["get", "post"],
            ["/api/hr/v1/employees/{id}/documents"] = ["get", "post"],
            ["/api/hr/v1/employee-documents/{id}/download"] = ["get"],
            ["/api/hr/v1/employee-documents/{id}/versions"] = ["post"],
            ["/api/hr/v1/employee-documents/{id}/archive"] = ["post"],
            ["/api/hr/v1/opening-imports"] = ["post"],
            ["/api/hr/v1/opening-imports/template"] = ["get"],
            ["/api/hr/v1/opening-imports/{id}"] = ["get"],
            ["/api/hr/v1/opening-imports/{id}/dry-run"] = ["post"],
            ["/api/hr/v1/opening-imports/{id}/execute"] = ["post"],
            ["/api/hr/v1/leave/requests/mine"] = ["get"],
            ["/api/hr/v1/leave/requests"] = ["get", "post"],
            ["/api/hr/v1/leave/requests/{requestId}"] = ["get"],
            ["/api/hr/v1/leave/requests/{requestId}/submit"] = ["post"],
            ["/api/hr/v1/leave/requests/{requestId}/cancel"] = ["post"],
            ["/api/hr/v1/leave/requests/{requestId}/attachments"] = ["get", "post"],
            ["/api/hr/v1/leave/requests/{requestId}/attachments/{versionId}"] = ["get"],
            ["/api/hr/v1/leave/approvals/{instanceId}/approve"] = ["post"],
            ["/api/hr/v1/leave/approvals/{instanceId}/reject"] = ["post"],
            ["/api/hr/v1/attendance/reports"] = ["get"],
            ["/api/hr/v1/overtime/policies"] = ["get", "post"],
            ["/api/hr/v1/overtime/calculate"] = ["post"],
            ["/api/hr/v1/overtime/entries"] = ["get"],
            ["/api/hr/v1/overtime/entries/{id}"] = ["get"],
            ["/api/hr/v1/overtime/entries/{id}/submit"] = ["post"],
            ["/api/hr/v1/overtime/entries/{id}/cancel"] = ["post"],
            ["/api/hr/v1/overtime/approvals/{instanceId}/approve"] = ["post"],
            ["/api/hr/v1/overtime/approvals/{instanceId}/reject"] = ["post"]
        };
        foreach (var (path, methods) in requiredRoutes)
        {
            Assert.True(swagger.Paths.ContainsKey(path), $"Missing OpenAPI path: {path}");
            var actual = swagger.Paths[path].Operations.Keys.Select(key => key.ToString().ToLowerInvariant());
            Assert.All(methods, method => Assert.Contains(method, actual));
        }
        Assert.DoesNotContain(swagger.Paths.Keys.Where(path => path.StartsWith("/api/hr/v1/", StringComparison.Ordinal)),
            path => new[] { "/payroll", "/salary", "/loans" }
                .Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase)));

        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var actions = typeof(HrEmployeesController).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(HrEmployeesController).Namespace)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(method => (Controller: type, Method: method)))
            .ToArray();
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            Assert.NotNull(action.Controller.GetCustomAttribute<AuthorizeAttribute>());
            var policies = action.Method.GetCustomAttributes<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy).OfType<string>().ToArray();
            if (action.Controller != typeof(HrPayrollCapabilitiesController)
                && action.Controller != typeof(HrLeaveController)
                && action.Method.Name != nameof(HrEmployeesController.GetCurrentEmployee))
                Assert.NotEmpty(policies);
            foreach (var policy in policies)
            {
                Assert.Contains(policy, PermissionCatalog.All);
                Assert.NotNull(await provider.GetPolicyAsync(policy));
            }
        }
    }

    [Fact]
    public async Task StartedApiDescribesPayslipRoutesAndResolvesTheirAuthorizationPolicies()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        const string route = "/api/payroll/v1/runs/{runId}/payslips/{employeeId}";
        var requiredRoutes = new Dictionary<string, string[]>
        {
            [route] = ["get", "post"],
            [$"{route}/download"] = ["get"],
            [$"{route}/print"] = ["get"],
            [$"{route}/email"] = ["post"]
        };
        foreach (var (path, methods) in requiredRoutes)
        {
            Assert.True(swagger.Paths.ContainsKey(path), $"Missing OpenAPI path: {path}");
            var actual = swagger.Paths[path].Operations.Keys.Select(key => key.ToString().ToLowerInvariant());
            Assert.All(methods, method => Assert.Contains(method, actual));
        }

        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var actions = typeof(RetailPOS.API.Controllers.Payroll.PayrollPayslipsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToArray();
        Assert.Equal(5, actions.Length);
        foreach (var action in actions)
        {
            var policy = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy).OfType<string>());
            Assert.Contains(policy, PermissionCatalog.All);
            Assert.NotNull(await provider.GetPolicyAsync(policy));
        }
    }

    [Fact]
    public async Task AnonymousRequestIsChallengedByRealBearerAuthentication()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var anonymous = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })));
        using var client = anonymous.CreateClient();
        using var response = await client.GetAsync("/api/hr/v1/employees");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("hr.employee.view", false)]
    public async Task CapabilityOrActionDenialFailsClosed(string permissions, bool enabled)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, permissions);
        await SeedAsync(factory.Services, enabled);
        using var response = await client.GetAsync("/api/hr/v1/employees");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CrossBusinessAndUnknownEmployeeIdsDoNotDiscloseProfiles()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, "hr.employee.view");
        await SeedAsync(factory.Services);
        using var foreign = await client.GetAsync("/api/hr/v1/employees/21");
        using var unknown = await client.GetAsync("/api/hr/v1/employees/999999");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(foreign.StatusCode, unknown.StatusCode);
        Assert.Equal(await foreign.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Other employee", await foreign.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingBusinessSelectionFailsClosed()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, "hr.employee.view");
        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "0");
        await SeedAsync(factory.Services);
        using var response = await client.GetAsync("/api/hr/v1/employees");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("HR-11", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConfidentialDocumentMetadataIsAuditedBeforeResponseAndNeverCached()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, "hr.employee_document.view,hr.employee.view_sensitive,hr.employee.view_identity");
        await SeedAsync(factory.Services);
        using var response = await client.GetAsync("/api/hr/v1/employees/11/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("CONFIDENTIAL-ID", body);
        Assert.DoesNotContain("storageKey", body, StringComparison.OrdinalIgnoreCase);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var audit = await db.AuditEvents.SingleAsync(row => row.ActionType == "hr.employee_document.metadata_accessed");
        Assert.Equal(1, audit.BusinessId);
        Assert.DoesNotContain("CONFIDENTIAL-ID", audit.ActionSummary);
    }

    [Fact]
    public async Task ConfidentialMetadataAuditFailureReleasesNoDocumentData()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var failedAudit = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<RetailPOSDbContext>(options =>
                options.AddInterceptors(new FailMetadataAuditInterceptor()))));
        using var client = failedAudit.CreateClient();
        Authorize(client, "hr.employee_document.view,hr.employee.view_sensitive,hr.employee.view_identity");
        await SeedAsync(failedAudit.Services);
        using var response = await client.GetAsync("/api/hr/v1/employees/11/documents");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("CONFIDENTIAL-ID", await response.Content.ReadAsStringAsync());
        using var scope = failedAudit.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
            .AuditEvents.AnyAsync(row => row.ActionType == "hr.employee_document.metadata_accessed"));
    }

    private static void Authorize(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessOwner");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }

    private static async Task SeedAsync(IServiceProvider services, bool enabled = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "Hardening API" },
            new Business { Id = 2, Name = "Other Business" });
        db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            { BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = enabled });
        db.Users.Add(new User { Id = 101, BusinessId = 1, Name = "HR actor", Email = "hr@example.invalid",
            PasswordHash = "test", IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAll });
        db.HrEmploymentTypes.AddRange(
            new HrEmploymentType { Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent" },
            new HrEmploymentType { Id = 2, BusinessId = 2, Code = "PERM", Name = "Permanent" });
        db.HrEmployees.AddRange(new HrEmployee { Id = 11, BusinessId = 1, EmployeeCode = "HR-11",
            FirstName = "Employee", DisplayName = "Employee", CurrentEmploymentTypeId = 1 },
            new HrEmployee { Id = 21, BusinessId = 2, EmployeeCode = "HR-21",
                FirstName = "Other", DisplayName = "Other employee", CurrentEmploymentTypeId = 2 });
        db.HrDocumentTypes.Add(new HrDocumentType { Id = 1, BusinessId = 1, Code = "ID",
            Name = "Identity", Classification = "Confidential", IsActive = true });
        db.HrEmployeeDocuments.Add(new HrEmployeeDocument { BusinessId = 1, EmployeeId = 11,
            DocumentTypeId = 1, DocumentNumber = "CONFIDENTIAL-ID", Status = "Active", Version = 1 });
        await db.SaveChangesAsync();
    }

    private sealed class FailMetadataAuditInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.ActionType == "hr.employee_document.metadata_accessed"))
                throw new IOException("Injected confidential metadata audit failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
