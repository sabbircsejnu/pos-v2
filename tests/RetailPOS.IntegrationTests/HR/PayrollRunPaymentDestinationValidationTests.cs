using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollRunPaymentDestinationValidationTests
{
    [Fact]
    public async Task ValidationUsesDestinationEffectiveAtPeriodEndAndNeverReturnsAccountValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        var persistedRun = new PayrollRun
        {
            Id = 1,
            BusinessId = 1,
            PayrollPeriodId = 1,
            RunNo = 1,
            RunType = PayrollRunTypes.Regular,
            ScopeType = PayrollRunScopeTypes.Business,
            ScopeKey = PayrollRunScopeTypes.BusinessKey,
            Status = PayrollRunStatuses.Draft,
            Version = 1,
            CalculationVersion = 0,
            IdempotencyKey = "validation",
            IdempotencyPayloadHash = ""
        };
        db.PayrollRuns.Add(persistedRun);
        db.PayrollRunEmployeeSelections.Add(new PayrollRunEmployeeSelection
        {
            Id = 1,
            BusinessId = 1,
            PayrollRunId = 1,
            EmployeeId = 15,
            EmployeeCodeSnapshot = "EMP-15",
            EmployeeNameSnapshot = "Employee 15",
            SalaryVersionId = 99,
            EligibilityDate = new DateOnly(2026, 10, 1),
            IsIncluded = true,
            SelectedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var run = new PayrollRunResponse(
            1, 1, 1, 1, PayrollRunTypes.Regular, PayrollRunScopeTypes.Business, null,
            PayrollRunStatuses.Draft, 1, "BDT", 2, "AwayFromZero", null,
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 1, 0,
            DateTime.UtcNow, 101);
        var store = new PayrollRunValidationStore(db);

        var missing = await store.ValidateAsync(1, 1, run, 1, 50, default);
        Assert.Contains(missing.Findings, finding =>
            finding.Code == "MissingPaymentDestination" && finding.EmployeeId == 15);

        db.PayrollEmployeePaymentDestinations.Add(new PayrollEmployeePaymentDestination
        {
            Id = 1, BusinessId = 1, EmployeeId = 15, VersionNo = 1, Version = 1,
            OperationalLocationProjectionId = 10, Method = PayrollPaymentDestinationMethods.Cash,
            IsPrimary = true, IsActive = true, EffectiveFrom = new DateOnly(2026, 10, 31),
            CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var cash = await store.ValidateAsync(1, 1, run, 1, 50, default);
        Assert.DoesNotContain(cash.Findings, finding =>
            finding.Category == "PaymentDetails" && finding.EmployeeId == 15);

        db.PayrollEmployeePaymentDestinations.Add(new PayrollEmployeePaymentDestination
        {
            Id = 2, BusinessId = 1, EmployeeId = 15, VersionNo = 2, Version = 2,
            OperationalLocationProjectionId = 10, Method = PayrollPaymentDestinationMethods.Bank,
            IsPrimary = true, IsActive = true, EffectiveFrom = new DateOnly(2026, 11, 1),
            BankName = "Bank", AccountName = "Employee", AccountNumber = "123456",
            CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var bank = await store.ValidateAsync(
            1, 1, run with { PeriodEnd = new DateOnly(2026, 11, 1) }, 1, 50, default);
        Assert.DoesNotContain(bank.Findings, finding =>
            finding.Category == "PaymentDetails" && finding.EmployeeId == 15);
        Assert.DoesNotContain(bank.Findings, finding =>
            finding.Message.Contains("Bank", StringComparison.Ordinal)
            || finding.Message.Contains("Employee", StringComparison.Ordinal)
            || finding.Message.Contains("123456", StringComparison.Ordinal));

        db.PayrollEmployeePaymentDestinations.Add(new PayrollEmployeePaymentDestination
        {
            Id = 3, BusinessId = 1, EmployeeId = 15, VersionNo = 3, Version = 3,
            OperationalLocationProjectionId = 10, Method = PayrollPaymentDestinationMethods.Mobile,
            IsPrimary = true, IsActive = true, EffectiveFrom = new DateOnly(2026, 11, 2),
            MobileProvider = "Provider", MobileAccountName = "Employee",
            MobileAccountNumber = "01700000000",
            CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var mobile = await store.ValidateAsync(
            1, 1, run with { PeriodEnd = new DateOnly(2026, 11, 2) }, 1, 50, default);
        Assert.DoesNotContain(mobile.Findings, finding =>
            finding.Category == "PaymentDetails" && finding.EmployeeId == 15);
        Assert.DoesNotContain(mobile.Findings, finding =>
            finding.Message.Contains("Provider", StringComparison.Ordinal)
            || finding.Message.Contains("Employee", StringComparison.Ordinal)
            || finding.Message.Contains("01700000000", StringComparison.Ordinal));

        var incompleteBank = new PayrollEmployeePaymentDestination
        {
            Id = 4, BusinessId = 1, EmployeeId = 15, VersionNo = 4, Version = 4,
            OperationalLocationProjectionId = 10, Method = PayrollPaymentDestinationMethods.Bank,
            IsPrimary = true, IsActive = true, EffectiveFrom = new DateOnly(2026, 11, 3),
            BankName = "Bank", AccountName = "Employee",
            CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
        };
        db.PayrollEmployeePaymentDestinations.Add(incompleteBank);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.Entry(incompleteBank).State = EntityState.Detached;
        await db.Database.ExecuteSqlRawAsync(
            "DROP INDEX ux_payroll_payment_destination_effective_from;");
        db.PayrollEmployeePaymentDestinations.AddRange(
            new PayrollEmployeePaymentDestination
            {
                Id = 5, BusinessId = 1, EmployeeId = 15, VersionNo = 4, Version = 4,
                OperationalLocationProjectionId = 10, Method = PayrollPaymentDestinationMethods.Mobile,
                IsPrimary = true, IsActive = true, EffectiveFrom = new DateOnly(2026, 11, 3),
                MobileProvider = "Provider", MobileAccountName = "Employee",
                MobileAccountNumber = "01700000000",
                CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
            },
            new PayrollEmployeePaymentDestination
            {
                Id = 6, BusinessId = 1, EmployeeId = 15, VersionNo = 5, Version = 5,
                OperationalLocationProjectionId = 10, Method = PayrollPaymentDestinationMethods.Cash,
                IsPrimary = true, IsActive = true, EffectiveFrom = new DateOnly(2026, 11, 3),
                CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        var ambiguous = await store.ValidateAsync(
            1, 1, run with { PeriodEnd = new DateOnly(2026, 11, 3) }, 1, 50, default);
        Assert.Contains(ambiguous.Findings, finding =>
            finding.Code == "AmbiguousPaymentDestination" && finding.EmployeeId == 15);
        Assert.DoesNotContain(ambiguous.Findings, finding =>
            finding.Message.Contains("Provider", StringComparison.Ordinal)
            || finding.Message.Contains("Employee", StringComparison.Ordinal));
    }
}
