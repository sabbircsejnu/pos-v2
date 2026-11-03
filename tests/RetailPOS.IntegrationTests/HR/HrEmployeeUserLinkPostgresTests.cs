using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Employees;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrEmployeeUserLinkPostgresTests
{
    [Fact]
    public async Task PostgreSqlUniqueIndexesSerializeConcurrentEmployeeAndUserLinks()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var settingsPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "RetailPOS.API", "appsettings.json"));
            connectionString = new ConfigurationBuilder()
                .AddJsonFile(settingsPath, optional: false)
                .Build()
                .GetConnectionString("DefaultConnection");
        }

        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        await using var database = await PostgresTestDatabase.CreateAsync(
            connectionString!, PostgresTestDatabasePrefix.MigrationBaseline);
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        long employeeId;
        long secondEmployeeId;
        long thirdEmployeeId;
        const long businessId = 98501;
        const long actorId = 98501;

        await using (var setup = new RetailPOSDbContext(options))
        {
            await setup.Database.MigrateAsync();
            var business = new Business { Id = businessId, Name = "Employee User Link Concurrency" };
            setup.Businesses.Add(business);
            await setup.SaveChangesAsync();
            var type = new HrEmploymentType
            {
                BusinessId = businessId,
                Code = "Permanent",
                Name = "Permanent",
                IsActive = true
            };
            setup.HrEmploymentTypes.Add(type);
            setup.Users.AddRange(
                MakeUser(actorId, businessId),
                MakeUser(98502, businessId),
                MakeUser(98503, businessId),
                MakeUser(98504, businessId));
            await setup.SaveChangesAsync();
            var employees = new[]
            {
                MakeEmployee(businessId, type.Id, "EMP-USERLINK-1"),
                MakeEmployee(businessId, type.Id, "EMP-USERLINK-2"),
                MakeEmployee(businessId, type.Id, "EMP-USERLINK-3")
            };
            setup.HrEmployees.AddRange(employees);
            await setup.SaveChangesAsync();
            employeeId = employees[0].Id;
            secondEmployeeId = employees[1].Id;
            thirdEmployeeId = employees[2].Id;
        }

        async Task<bool> TryLink(long targetEmployeeId, long identityUserId)
        {
            var auditContext = new AuditContext();
            auditContext.Initialize(new AuditContextSnapshot(
                Guid.NewGuid(), actorId, actorId, null, null, null, businessId, AuditSource.API,
                "POST", "/api/hr/v1/employees/user-link", null, null, null, false,
                null, null, null, null, "employee-user-link-postgres", "HR"));
            await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(database.ConnectionString)
                .AddInterceptors(new AuditChangeTrackerInterceptor(
                    auditContext, NullLogger<AuditChangeTrackerInterceptor>.Instance))
                .Options);
            var auditService = new AuditService(
                db, auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
            var audit = new RequiredAuditTransaction(
                db, auditContext, auditService, NullLogger<RequiredAuditTransaction>.Instance);
            var handler = new LinkHrEmployeeUserCommandHandler(
                new TestAccess(businessId, actorId), new HrEmployeeStore(db),
                new IdentityUserDirectory(db), audit);
            try
            {
                return await handler.Handle(
                    new LinkHrEmployeeUserCommand(targetEmployeeId, identityUserId, "Concurrent validation."),
                    CancellationToken.None) is not null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var employeeRace = await Task.WhenAll(
            TryLink(employeeId, 98502), TryLink(employeeId, 98503));
        Assert.Equal(1, employeeRace.Count(result => result));
        await using (var verify = new RetailPOSDbContext(options))
        {
            Assert.Equal(1, await verify.HrEmployeeUserLinks.CountAsync(link =>
                link.BusinessId == businessId && link.EmployeeId == employeeId && link.IsActive));
        }

        var userRace = await Task.WhenAll(
            TryLink(secondEmployeeId, 98504),
            TryLink(thirdEmployeeId, 98504));
        Assert.Equal(1, userRace.Count(result => result));
        await using (var verify = new RetailPOSDbContext(options))
        {
            Assert.Equal(1, await verify.HrEmployeeUserLinks.CountAsync(link =>
                link.IdentityUserId == 98504 && link.IsActive));
        }
    }

    private static User MakeUser(long id, long businessId) => new()
    {
        Id = id,
        BusinessId = businessId,
        Name = $"User {id}",
        Email = $"{id}@example.invalid",
        PasswordHash = "test-hash",
        IsActive = true
    };

    private static HrEmployee MakeEmployee(long businessId, long employmentTypeId, string code) => new()
    {
        BusinessId = businessId,
        EmployeeCode = code,
        FirstName = "Test",
        DisplayName = code,
        CurrentEmploymentStatus = "Active",
        CurrentEmploymentTypeId = employmentTypeId,
        JoiningDate = new DateOnly(2026, 10, 1),
        Version = 1
    };

    private sealed class TestAccess(long businessId, long actorId) : IHrEmployeeAccess
    {
        public long RequireBusinessId() => businessId;
        public long RequireRealUserId() => actorId;
        public HrEmployeeFieldAccess GetFieldAccess() => new(false, false);
        public Task EnsureAsync(long requestedBusinessId, HrEmployeeOperation operation,
            CancellationToken cancellationToken) =>
            requestedBusinessId == businessId
                ? Task.CompletedTask
                : throw new UnauthorizedAccessException();
        public Task EnsurePositionAssignmentAsync(long requestedBusinessId, long? locationProjectionId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
