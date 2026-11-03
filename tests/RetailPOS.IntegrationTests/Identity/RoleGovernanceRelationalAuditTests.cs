using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.API.DTOs.Roles;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Identity;

public sealed class RoleGovernanceRelationalAuditTests : IClassFixture<SqliteRoleGovernanceApiFactory>
{
    private readonly SqliteRoleGovernanceApiFactory _factory;

    public RoleGovernanceRelationalAuditTests(SqliteRoleGovernanceApiFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task CreateUpdateAndDelete_WriteAdminAuditEvidenceWithActorAndSnapshots()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();

        using var createResponse = await client.PostAsJsonAsync("/api/roles", new CreateRoleDto
        {
            Name = "Relational Role",
            Permissions = ["sales.view"]
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(created);

        using var updateResponse = await client.PutAsJsonAsync($"/api/roles/{created.Id}", new UpdateRoleDto
        {
            Name = "Relational Role Updated",
            Permissions = ["products.view"]
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var deleteResponse = await client.DeleteAsync($"/api/roles/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var roleAudits = await ReadRoleAdminAuditsAsync();
        Assert.Equal(3, roleAudits.Count);

        var createAudit = AssertAudit(roleAudits[0], AuditActionType.Create, created.Id, AuditOperationType.Insert);
        AssertField(createAudit, "Name", null, "Relational Role");
        AssertField(createAudit, "Permissions", null, """["sales.view"]""");

        var updateAudit = AssertAudit(roleAudits[1], AuditActionType.PermissionChange, created.Id, AuditOperationType.Update);
        AssertField(updateAudit, "Name", "Relational Role", "Relational Role Updated");
        AssertField(updateAudit, "Permissions", """["sales.view"]""", """["products.view"]""");

        var deleteAudit = AssertAudit(roleAudits[2], AuditActionType.Delete, created.Id, AuditOperationType.Delete);
        AssertField(deleteAudit, "Name", "Relational Role Updated", null);
        AssertField(deleteAudit, "Permissions", """["products.view"]""", null);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(await db.Roles.AnyAsync(role => role.Id == created.Id));
    }

    [Fact]
    public async Task AuditPersistenceFailure_RollsBackRoleUpdateAndDoesNotLeaveSuccessAudit()
    {
        await ResetDatabaseAsync();
        const long roleId = 40;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Roles.Add(new Role
            {
                Id = roleId,
                Name = "Before Failure",
                Permissions = """["sales.view"]"""
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient();
        _factory.RoleAuditFailureInterceptor.FailAdminRoleAudit = true;
        try
        {
            using var response = await client.PutAsJsonAsync($"/api/roles/{roleId}", new UpdateRoleDto
            {
                Name = "Must Roll Back",
                Permissions = ["products.view"]
            });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(
                "An error occurred while updating the role",
                (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Error);
        }
        finally
        {
            _factory.RoleAuditFailureInterceptor.FailAdminRoleAudit = false;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var persistedRole = await db.Roles.SingleAsync(role => role.Id == roleId);
            Assert.Equal("Before Failure", persistedRole.Name);
            Assert.Equal("""["sales.view"]""", persistedRole.Permissions);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.AuditEvents
                .Where(audit =>
                    audit.PrimaryEntityType == "Role" &&
                    audit.PrimaryEntityId == roleId.ToString())
                .ToListAsync());
        }
    }

    [Fact]
    public async Task InvalidPermissions_DoNotWriteRoleOrAdminSuccessAudit()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();

        using var response = await client.PostAsJsonAsync("/api/roles", new CreateRoleDto
        {
            Name = "Invalid Relational Role",
            Permissions = ["invalid.permission"]
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(await db.Roles.AnyAsync(role => role.Name == "Invalid Relational Role"));
        Assert.Empty(await ReadRoleAdminAuditsAsync());
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add(
            "X-Test-Permissions",
            "roles.view,roles.create,roles.edit,roles.delete");
        return client;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        _factory.RoleAuditFailureInterceptor.FailAdminRoleAudit = false;
    }

    private async Task<List<AuditEvent>> ReadRoleAdminAuditsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.AuditEvents
            .Include(audit => audit.Entities)
            .ThenInclude(entity => entity.FieldChanges)
            .Where(audit =>
                audit.Module == AuditModule.Admin &&
                audit.PrimaryEntityType == "Role")
            .OrderBy(audit => audit.Id)
            .ToListAsync();
    }

    private static AuditEventEntity AssertAudit(
        AuditEvent audit,
        string actionType,
        long roleId,
        string operationType)
    {
        Assert.Equal(actionType, audit.ActionType);
        Assert.Equal(AuditModule.Admin, audit.Module);
        Assert.Equal(AuditStatus.Success, audit.Status);
        Assert.Equal(101L, audit.RealUserId);
        Assert.Equal("Role", audit.PrimaryEntityType);
        Assert.Equal(roleId.ToString(), audit.PrimaryEntityId);
        Assert.NotEqual(default, audit.CreatedAt);

        var entity = Assert.Single(audit.Entities);
        Assert.Equal("Role", entity.EntityType);
        Assert.Equal(roleId.ToString(), entity.EntityId);
        Assert.Equal(operationType, entity.OperationType);
        Assert.Equal(2, entity.FieldsChangedCount);
        Assert.NotEqual(default, entity.CreatedAt);
        return entity;
    }

    private static void AssertField(
        AuditEventEntity entity,
        string fieldName,
        string? expectedOldValue,
        string? expectedNewValue)
    {
        var field = Assert.Single(entity.FieldChanges, change => change.FieldName == fieldName);
        AssertJsonValue(expectedOldValue, field.OldValue);
        AssertJsonValue(expectedNewValue, field.NewValue);
        Assert.NotEqual(default, field.CreatedAt);
    }

    private static void AssertJsonValue(string? expected, string? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), actual);
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}

public class SqliteRoleGovernanceApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public RoleAuditFailureInterceptor RoleAuditFailureInterceptor { get; } = new();
    public IInterceptor? AdditionalDbCommandInterceptor { get; set; }
    public IPayrollPeriodCloseExceptionPolicy? PayrollPeriodCloseExceptionPolicy { get; set; }

    public SqliteRoleGovernanceApiFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        TestJwtConfiguration.Configure(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
            services.RemoveAll<RetailPOSDbContext>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));
            services.AddSingleton(RoleAuditFailureInterceptor);
            if (PayrollPeriodCloseExceptionPolicy is { } closeExceptionPolicy)
            {
                services.RemoveAll<IPayrollPeriodCloseExceptionPolicy>();
                services.AddSingleton(closeExceptionPolicy);
            }
            services.AddDbContext<RetailPOSDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(_connection)
                    .AddInterceptors(
                        serviceProvider.GetRequiredService<AuditChangeTrackerInterceptor>(),
                        serviceProvider.GetRequiredService<RoleAuditFailureInterceptor>());
                if (AdditionalDbCommandInterceptor is { } interceptor)
                    options.AddInterceptors(interceptor);
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

public sealed class RoleAuditFailureInterceptor : SaveChangesInterceptor
{
    public bool FailAdminRoleAudit { get; set; }
    public bool FailPayrollAdjustmentAudit { get; set; }
    public bool FailPayrollLoanAudit { get; set; }
    public bool FailPayrollRunCalculationAudit { get; set; }
    public bool FailPayrollRunValidationAudit { get; set; }
    public bool FailPayrollRunCorrectionAudit { get; set; }
    public bool FailApprovalSubmissionAudit { get; set; }
    public bool FailApprovalDecisionAudit { get; set; }
    public string? FailAuditActionType { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (FailAuditActionType is { } actionType &&
            eventData.Context?.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State == EntityState.Added && entry.Entity.ActionType == actionType) == true)
        {
            throw new IOException($"Injected {actionType} audit persistence failure.");
        }
        if (FailAdminRoleAudit &&
            eventData.Context?.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.Module == AuditModule.Admin &&
                entry.Entity.PrimaryEntityType == "Role") == true)
        {
            throw new IOException("Injected Role audit persistence failure.");
        }
        if (FailPayrollAdjustmentAudit &&
            eventData.Context?.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.ActionType.StartsWith("payroll.allowance_deduction.", StringComparison.Ordinal)) == true)
        {
            throw new IOException("Injected payroll adjustment audit persistence failure.");
        }
        if (FailPayrollLoanAudit &&
            eventData.Context?.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.ActionType.StartsWith("payroll.loan.", StringComparison.Ordinal)) == true)
        {
            throw new IOException("Injected employee loan audit persistence failure.");
        }
        if (FailPayrollRunCalculationAudit &&
            eventData.Context?.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.ActionType == "payroll.run.calculated") == true)
        {
            throw new IOException("Injected Payroll run calculation audit persistence failure.");
        }
        if (FailPayrollRunValidationAudit &&
            eventData.Context?.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.ActionType == "payroll.run.validation_accessed") == true)
        {
            throw new IOException("Injected Payroll run validation audit persistence failure.");
        }
        if (FailPayrollRunCorrectionAudit &&
            eventData.Context?.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.ActionType == "payroll.run.correction_created") == true)
        {
            throw new IOException("Injected Payroll correction audit persistence failure.");
        }
        if (FailApprovalDecisionAudit &&
            eventData.Context?.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.ActionType == "hr.approval.decided") == true)
        {
            throw new IOException("Injected approval decision audit persistence failure.");
        }
        if (FailApprovalSubmissionAudit &&
            eventData.Context?.ChangeTracker.Entries<AuditEvent>().Any(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.ActionType == "hr.approval.submitted") == true)
        {
            throw new IOException("Injected approval submission audit persistence failure.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
