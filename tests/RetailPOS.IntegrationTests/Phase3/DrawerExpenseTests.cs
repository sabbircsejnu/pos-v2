using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using RetailPOS.API.Services;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Expenses.Commands;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Expenses;
using RetailPOS.Infrastructure.Integration;
using RetailPOS.Infrastructure.Pos;
using RetailPOS.Tests.Infrastructure;
using System.Security.Claims;

namespace RetailPOS.IntegrationTests.Phase3;

/// <summary>D7: independent approval for POS Drawer / Petty Cash, the audited owner exception and the drawer payout step.</summary>
public sealed class DrawerExpenseTests
{
    static DrawerExpenseTests() => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    private static Task RunAsync(Func<PostgresTestDatabase, Task> operation) =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.ExpenseApproval,
            async database =>
            {
                await using (var db = Ctx(database))
                {
                    await db.Database.MigrateAsync();
                    db.Businesses.Add(new Business { Id = 1, Name = "B1", IsActive = true });
                    await db.SaveChangesAsync();
                    db.Outlets.Add(new Outlet { Id = 10, BusinessId = 1, Name = "Main", InvoicePrefix = "M" });
                    foreach (var id in new long[] { 1, 2, 3 })
                        db.Users.Add(new User { Id = id, BusinessId = 1, Name = $"U{id}", Email = $"u{id}@t.test", PasswordHash = "x", OutletId = 10, IsActive = true });
                    db.PosTerminals.Add(new PosTerminal { Id = 20, OutletId = 10, Name = "C1", Code = "C1", IsActive = true, IsDefault = true });
                    await db.SaveChangesAsync();
                }

                await operation(database);
            });

    private static RetailPOSDbContext Ctx(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

    private static async Task<long> OpenSessionAsync(PostgresTestDatabase database)
    {
        await using var db = Ctx(database);
        var session = new CashSession
        {
            BusinessId = 1, OutletId = 10, TerminalId = 20, BusinessDate = DateTime.UtcNow.Date, OpenedAt = DateTime.UtcNow,
            OpeningCashAmount = 500m, Status = CashSessionStatuses.Open, OpenedByUserId = 1, TimeZoneIdSnapshot = "UTC", DurationSnapshotHours = 20
        };
        db.CashSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static async Task<long> SeedExpenseAsync(
        PostgresTestDatabase database, string? source, string status = ExpenseStatuses.Approved, long createdBy = 1,
        long? submittedBy = 1, long? approvedBy = 2, decimal amount = 40m)
    {
        await using var db = Ctx(database);
        var expense = new Expense
        {
            BusinessId = 1, Category = "Supplies", Amount = amount, ExpenseDate = DateTime.UtcNow.Date, OutletId = 10,
            Status = status, PaymentSource = source, CreatedBy = createdBy, SubmittedBy = submittedBy,
            ApprovedBy = status == ExpenseStatuses.Approved ? approvedBy : null, ExpenseNumber = $"EXP-{Guid.NewGuid():N}"[..20]
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return expense.Id;
    }

    private static PayDrawerExpenseCommandHandler PayHandler(RetailPOSDbContext db)
    {
        var authorizer = new Mock<ICashSessionWriteOutletAuthorizer>();
        authorizer.Setup(a => a.AuthorizeWriteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((long o, CancellationToken _) => o);
        var store = new CashSessionStore(db);
        var events = new CashSessionEventWriter(new IntegrationOutbox(
            db, Mock.Of<IAuditContext>(), TimeProvider.System, Mock.Of<IIntegrationReplayAuthorization>()));
        return new PayDrawerExpenseCommandHandler(
            new ExpenseDrawerStore(db), store, new CashSessionMovementRecorder(db, store, events, TimeProvider.System),
            events, Mock.Of<ICashSessionAuditWriter>(), authorizer.Object, TimeProvider.System);
    }

    [Fact]
    public Task Approved_drawer_expense_is_paid_once_from_the_open_session_and_reduces_expected_cash() => RunAsync(async database =>
    {
        var sessionId = await OpenSessionAsync(database);
        var expenseId = await SeedExpenseAsync(database, ExpensePaymentSources.PosDrawer);

        await using (var db = Ctx(database))
        {
            var first = await PayHandler(db).Handle(new PayDrawerExpenseCommand(1, 3, expenseId, 20), CancellationToken.None);
            Assert.False(first.Replayed);
            Assert.Equal(sessionId, first.CashSessionId);
        }

        await using (var db = Ctx(database))
        {
            var retry = await PayHandler(db).Handle(new PayDrawerExpenseCommand(1, 3, expenseId, 20), CancellationToken.None);
            Assert.True(retry.Replayed);
        }

        await using var verify = Ctx(database);
        var movement = await verify.CashSessionMovements.SingleAsync();
        Assert.Equal(-40m, movement.CashEffect);
        Assert.Equal(CashSessionMovementTypes.DrawerExpense, movement.MovementType);
        var expense = await verify.Expenses.SingleAsync();
        Assert.Equal(sessionId, expense.DisbursedCashSessionId);
        Assert.NotNull(expense.DisbursedAtUtc);

        var facts = await new CashSessionStore(verify).GetFactsAsync(sessionId, CancellationToken.None);
        Assert.Equal(460m, CashSessionExpectedCashCalculator.Calculate(facts!, DateTime.UtcNow).ExpectedClosingCash);
    });

    [Fact]
    public Task Petty_cash_bank_and_unapproved_expenses_never_reach_the_drawer() => RunAsync(async database =>
    {
        await OpenSessionAsync(database);
        foreach (var (source, status) in new[]
                 {
                     (ExpensePaymentSources.PettyCash, ExpenseStatuses.Approved),
                     (ExpensePaymentSources.Bank, ExpenseStatuses.Approved),
                     ((string?)null, ExpenseStatuses.Approved),
                     (ExpensePaymentSources.PosDrawer, ExpenseStatuses.Submitted)
                 })
        {
            var expenseId = await SeedExpenseAsync(database, source, status);
            await using var db = Ctx(database);
            var error = await Assert.ThrowsAsync<CashSessionException>(() =>
                PayHandler(db).Handle(new PayDrawerExpenseCommand(1, 3, expenseId, 20), CancellationToken.None));
            Assert.Equal(CashSessionErrorCodes.ExpenseNotPayable, error.Code);
        }

        await using var verify = Ctx(database);
        Assert.Empty(await verify.CashSessionMovements.ToListAsync());
    });

    [Fact]
    public Task A_drawer_payment_is_never_attached_to_a_closed_historical_session() => RunAsync(async database =>
    {
        var sessionId = await OpenSessionAsync(database);
        await using (var db = Ctx(database))
        {
            await db.CashSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, CashSessionStatuses.Closed));
        }

        var expenseId = await SeedExpenseAsync(database, ExpensePaymentSources.PosDrawer);
        await using var context = Ctx(database);
        var error = await Assert.ThrowsAsync<CashSessionException>(() =>
            PayHandler(context).Handle(new PayDrawerExpenseCommand(1, 3, expenseId, 20), CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.NoActiveSession, error.Code);
        Assert.Empty(await Ctx(database).CashSessionMovements.ToListAsync());
    });

    [Fact]
    public Task The_database_rejects_self_approved_drawer_and_petty_cash_expenses_without_the_audited_exception() => RunAsync(async database =>
    {
        await using var db = Ctx(database);
        var ok = new Expense
        {
            BusinessId = 1, Category = "c", Amount = 1, ExpenseDate = DateTime.UtcNow, Status = ExpenseStatuses.Approved,
            PaymentSource = ExpensePaymentSources.PettyCash, CreatedBy = 1, SubmittedBy = 1, ApprovedBy = 2
        };
        db.Expenses.Add(ok);
        await db.SaveChangesAsync();

        var selfApproved = new Expense
        {
            BusinessId = 1, Category = "c", Amount = 1, ExpenseDate = DateTime.UtcNow, Status = ExpenseStatuses.Approved,
            PaymentSource = ExpensePaymentSources.PosDrawer, CreatedBy = 1, SubmittedBy = 1, ApprovedBy = 1
        };
        db.Expenses.Add(selfApproved);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("ck_expenses_maker_checker", Assert.IsType<PostgresException>(exception.InnerException).ConstraintName);

        db.ChangeTracker.Clear();
        db.Expenses.Add(new Expense
        {
            BusinessId = 1, Category = "c", Amount = 1, ExpenseDate = DateTime.UtcNow, Status = ExpenseStatuses.Approved,
            PaymentSource = ExpensePaymentSources.PosDrawer, CreatedBy = 1, SubmittedBy = 1, ApprovedBy = 1,
            SelfApprovedUnderOwnerException = true
        });
        await db.SaveChangesAsync();
    });

    // ── the real policy ────────────────────────────────────────────────────────────────────────────────────────────

    private static ExpenseMakerCheckerPolicy Policy(bool exceptionEnabled, bool owner, params string[] permissions)
    {
        var settings = new Mock<IBusinessPosSettingsStore>();
        settings.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessPosSettingsSnapshot(
                1, 20, "UTC", true, 0m, "VAT", false, 0m, 1, null, null, true, exceptionEnabled));
        var role = new Mock<IRoleSwitchContext>();
        role.Setup(r => r.IsBusinessOwner).Returns(owner);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(permissions.Select(p => new Claim("permission", p)), "test"))
        };
        return new ExpenseMakerCheckerPolicy(new HttpContextAccessor { HttpContext = context }, role.Object, settings.Object);
    }

    private static Expense Own(string? source, long createdBy = 5, long? submittedBy = 5) =>
        new() { BusinessId = 1, PaymentSource = source, CreatedBy = createdBy, SubmittedBy = submittedBy };

    [Theory]
    [InlineData(ExpensePaymentSources.PosDrawer)]
    [InlineData(ExpensePaymentSources.PettyCash)]
    public async Task Creator_and_submitter_cannot_approve_even_with_a_wildcard_or_the_approve_permission(string source)
    {
        var policy = Policy(exceptionEnabled: true, owner: true, "*", "accounting.expenses.approve", "expenses.edit");

        var error = await Assert.ThrowsAsync<CashSessionException>(() => policy.EvaluateAsync(Own(source), 5, CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.ExpenseMakerChecker, error.Code);
    }

    [Fact]
    public async Task A_submitter_who_is_not_the_creator_is_also_blocked()
    {
        var policy = Policy(false, false, "accounting.expenses.approve");

        await Assert.ThrowsAsync<CashSessionException>(() =>
            policy.EvaluateAsync(Own(ExpensePaymentSources.PosDrawer, createdBy: 1, submittedBy: 5), 5, CancellationToken.None));
    }

    [Fact]
    public async Task An_independent_approver_is_allowed_without_the_exception_flag()
    {
        var policy = Policy(false, false, "accounting.expenses.approve");

        Assert.False(await policy.EvaluateAsync(Own(ExpensePaymentSources.PosDrawer), 9, CancellationToken.None));
    }

    [Fact]
    public async Task Bank_and_legacy_expenses_are_not_subject_to_maker_checker()
    {
        var policy = Policy(false, false);

        Assert.False(await policy.EvaluateAsync(Own(ExpensePaymentSources.Bank), 5, CancellationToken.None));
        Assert.False(await policy.EvaluateAsync(Own(null), 5, CancellationToken.None));
    }

    [Fact]
    public async Task Owner_exception_needs_the_setting_the_owner_role_and_the_exact_permission()
    {
        var expense = Own(ExpensePaymentSources.PosDrawer);

        Assert.True(await Policy(true, true, "expenses.self_approve").EvaluateAsync(expense, 5, CancellationToken.None));
        await Assert.ThrowsAsync<CashSessionException>(() => Policy(false, true, "expenses.self_approve").EvaluateAsync(expense, 5, CancellationToken.None));
        await Assert.ThrowsAsync<CashSessionException>(() => Policy(true, false, "expenses.self_approve").EvaluateAsync(expense, 5, CancellationToken.None));
        await Assert.ThrowsAsync<CashSessionException>(() => Policy(true, true, "*").EvaluateAsync(expense, 5, CancellationToken.None));
    }
}
