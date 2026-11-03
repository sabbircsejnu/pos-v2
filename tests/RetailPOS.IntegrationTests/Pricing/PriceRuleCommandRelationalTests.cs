using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pricing.Commands;
using RetailPOS.Application.Pricing.ReadModels;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Pricing;

namespace RetailPOS.Tests.Pricing;

public sealed class PriceRuleCommandRelationalTests
{
    [Fact]
    public async Task UpdateAndDeleteCommandsPersistOnSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new RetailPOSDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(dbContext, 1);

        var store = new PriceRuleCommandStore(dbContext, new TestBusinessScope(1));
        var created = await new CreatePriceRuleCommandHandler(store).Handle(
            new CreatePriceRuleCommand(Rule(" Initial ", "global", null, "fixed", 5m)),
            CancellationToken.None);

        var updated = await new UpdatePriceRuleCommandHandler(store).Handle(
            new UpdatePriceRuleCommand(
                created.Id,
                Rule(" Updated ", "GLOBAL", null, "PERCENTAGE", 15m)),
            CancellationToken.None);

        Assert.Equal("Updated", updated.Name);
        Assert.Equal("global", updated.RuleType);
        Assert.Equal("percentage", updated.DiscountType);
        Assert.Equal("Updated", (await dbContext.PriceRules.AsNoTracking().SingleAsync()).Name);

        await new DeletePriceRuleCommandHandler(store).Handle(
            new DeletePriceRuleCommand(created.Id),
            CancellationToken.None);

        Assert.Empty(await dbContext.PriceRules.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateRule_PersistsOnSqliteAndDuplicateRejectionLeavesNoExtraRow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new RetailPOSDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(dbContext, 1);

        var store = new PriceRuleCommandStore(dbContext, new TestBusinessScope(1));
        var handler = new CreatePriceRuleCommandHandler(store);

        var created = await handler.Handle(
            new CreatePriceRuleCommand(new PriceRuleWriteData(
                "Spring offer",
                "Seasonal",
                "GLOBAL",
                null,
                "PERCENTAGE",
                10m,
                1,
                0,
                null,
                null,
                true)),
            CancellationToken.None);

        Assert.True(created.Id > 0);
        Assert.Equal("Spring offer", created.Name);

        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                new CreatePriceRuleCommand(new PriceRuleWriteData(
                    "spring offer",
                    null,
                    "global",
                    null,
                    "percentage",
                    5m,
                    1,
                    0,
                    null,
                    null,
                    true)),
                CancellationToken.None));

        Assert.Equal("A price rule named 'spring offer' already exists", duplicate.Message);
        Assert.Equal(1, await dbContext.PriceRules.AsNoTracking().CountAsync());
    }

    private static PriceRuleWriteData Rule(
        string name,
        string ruleType,
        long? targetId,
        string discountType,
        decimal discountValue) =>
        new(
            name,
            null,
            ruleType,
            targetId,
            discountType,
            discountValue,
            1,
            0,
            null,
            null,
            true);
}
