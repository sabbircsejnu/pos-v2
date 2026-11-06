using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Commands;
using RetailPOS.Application.Customers.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Customers;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Customers;

/// <summary>
/// POS Quick Customer and POS customer search on a real PostgreSQL schema built from the tracked migration chain
/// (isolated Test stack only): normalized-phone uniqueness per Business under concurrent creation, and the
/// name/phone-only POS search.
/// </summary>
public sealed class PosQuickCustomerPostgresTests
{
    private static Task RunAsync(Func<PostgresTestDatabase, Task> operation) =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.SaleCreation,
            PostgresTestDatabaseStrategy.FreshMigration,
            operation);

    private static RetailPOSDbContext Context(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

    private static async Task SeedAsync(PostgresTestDatabase database)
    {
        await using var db = Context(database);
        await db.Database.MigrateAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        await db.SaveChangesAsync();
        db.Customers.AddRange(
            new Customer { Id = 101, BusinessId = 1, Name = "Ayesha Rahman", Phone = "01711000001", PhoneNormalized = "+8801711000001", Email = "ayesha@example.test" },
            new Customer { Id = 102, BusinessId = 1, Name = "Inactive Ayesha", Phone = "01711000002", PhoneNormalized = "+8801711000002", IsActive = false },
            new Customer { Id = 103, BusinessId = 1, Name = "Karim", Phone = "01811000003", PhoneNormalized = "+8801811000003", Email = "only-in-email-ayesha@example.test" },
            new Customer { Id = 201, BusinessId = 2, Name = "Foreign Ayesha", Phone = "01711000001", PhoneNormalized = "+8801711000001" });
        await db.SaveChangesAsync();
    }

    private static async Task<CustomerReadModel> CreateAsync(PostgresTestDatabase database, long businessId, string name, string phone)
    {
        await using var db = Context(database);
        var handler = new CreateCustomerCommandHandler(new CustomerCreateStore(db, new TestBusinessScope(businessId)));
        return await handler.Handle(new CreateCustomerCommand(name, phone, null), CancellationToken.None);
    }

    [Fact]
    public Task ConcurrentQuickCustomersWithOnePhone_CreateExactlyOneAndReportTheWinnerToTheRest() => RunAsync(async database =>
    {
        await SeedAsync(database);
        const int attempts = 8;
        using var start = new SemaphoreSlim(0, attempts);

        // The same number typed in different formats normalizes to one identity.
        var formats = new[] { "01911000009", "+8801911000009", "8801911000009", "+880 1911-000009" };
        var tasks = Enumerable.Range(0, attempts).Select(async index =>
        {
            await start.WaitAsync();
            try
            {
                return (Created: await CreateAsync(database, 1, $"Cashier entry {index}", formats[index % formats.Length]), Conflict: (CustomerPhoneConflictException?)null);
            }
            catch (CustomerPhoneConflictException conflict)
            {
                return (Created: (CustomerReadModel?)null, Conflict: conflict);
            }
        }).ToArray();
        start.Release(attempts);
        var outcomes = await Task.WhenAll(tasks);

        var winner = Assert.Single(outcomes, outcome => outcome.Created is not null).Created!;
        Assert.All(outcomes.Where(outcome => outcome.Conflict is not null),
            outcome => Assert.Equal(winner.Id, outcome.Conflict!.Existing.Id));
        Assert.Equal(attempts - 1, outcomes.Count(outcome => outcome.Conflict is not null));

        await using var db = Context(database);
        var rows = await db.Customers.AsNoTracking().Where(customer => customer.PhoneNormalized == "+8801911000009").ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(1, row.BusinessId);
        Assert.Equal(0, row.LoyaltyPoints);
        Assert.Null(row.Email);
        Assert.False(row.IsSystem);
    });

    [Fact]
    public Task QuickCustomer_PhoneIsUniquePerBusinessOnlyAndExistingOwnerIsReported() => RunAsync(async database =>
    {
        await SeedAsync(database);

        // A differently formatted copy of an existing number in the same Business is rejected with the owner.
        var duplicate = await Assert.ThrowsAsync<CustomerPhoneConflictException>(
            () => CreateAsync(database, 1, "Second Ayesha", "+880 1711-000001"));
        Assert.Equal(101, duplicate.Existing.Id);

        // Uniqueness is per Business: the same new number can be registered once in each Business.
        var created = await CreateAsync(database, 1, "New Number Owner", "01611000007");
        var sameNumberOtherBusiness = await CreateAsync(database, 2, "Other Business Owner", "01611000007");
        Assert.NotEqual(created.Id, sameNumberOtherBusiness.Id);

        await using var db = Context(database);
        var rows = await db.Customers.AsNoTracking()
            .Where(customer => customer.PhoneNormalized == "+8801611000007")
            .OrderBy(customer => customer.BusinessId)
            .Select(customer => customer.BusinessId)
            .ToListAsync();
        Assert.Equal(new long[] { 1, 2 }, rows);
        Assert.Equal(2, await db.Customers.CountAsync(customer => customer.PhoneNormalized == "+8801711000001"));
    });

    [Fact]
    public Task PosSearch_MatchesNameAndPhoneButNeverEmail() => RunAsync(async database =>
    {
        await SeedAsync(database);
        await using var db = Context(database);
        var handler = new PosQuickSearchCustomersQueryHandler(new CustomerReadStore(db, new TestBusinessScope(1)));

        async Task<long[]> SearchAsync(string query) =>
            (await handler.Handle(new PosQuickSearchCustomersQuery(query), CancellationToken.None)).Select(c => c.Id).ToArray();

        Assert.Equal(new long[] { 101 }, await SearchAsync("ayesha"));
        Assert.Equal(new long[] { 101 }, await SearchAsync("01711000001"));
        Assert.Equal(new long[] { 101 }, await SearchAsync("+880 1711-000001"));
        Assert.Equal(new long[] { 103 }, await SearchAsync("1811000"));
        Assert.Empty(await SearchAsync("ayesha@example"));
        Assert.Empty(await SearchAsync("only-in-email"));
        Assert.Empty(await SearchAsync("   "));

        // Customer Management quick search is unchanged and still matches email.
        var management = new QuickSearchCustomersQueryHandler(new CustomerReadStore(db, new TestBusinessScope(1)));
        Assert.Contains(
            await management.Handle(new QuickSearchCustomersQuery("only-in-email"), CancellationToken.None),
            customer => customer.Id == 103);
    });
}
