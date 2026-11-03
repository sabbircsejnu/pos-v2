using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class P306SettlementTransferHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public P306SettlementTransferHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task SettlementAndTransferListsRequireDedicatedPermissionsAndRemainBusinessScoped()
    {
        await SeedAsync();
        using var missing = Client(1, "accounting.reports.general_ledger");
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/settlements")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/settlements/2001")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/liquidity-transfers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/liquidity-transfers/3001")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/settlements", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/settlements/2001/reverse", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/liquidity-transfers", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/liquidity-transfers/3001/reverse", new { reason = "x" })).StatusCode);

        using var tenantOne = Client(1, "accounting.settlements.view,accounting.liquidity_transfers.view");
        var settlements = await tenantOne.GetStringAsync("/api/accounting/settlements");
        var transfers = await tenantOne.GetStringAsync("/api/accounting/liquidity-transfers");
        Assert.Contains("tenant-one-settlement", settlements, StringComparison.Ordinal);
        Assert.Contains("tenant-one-latest-settlement", settlements, StringComparison.Ordinal);
        Assert.Contains("tenant-one-transfer", transfers, StringComparison.Ordinal);
        Assert.Contains("tenant-one-latest-transfer", transfers, StringComparison.Ordinal);
        Assert.True(
            settlements.IndexOf("tenant-one-latest-settlement", StringComparison.Ordinal)
                < settlements.IndexOf("tenant-one-settlement", StringComparison.Ordinal));
        Assert.True(
            transfers.IndexOf("tenant-one-latest-transfer", StringComparison.Ordinal)
                < transfers.IndexOf("tenant-one-transfer", StringComparison.Ordinal));
        Assert.DoesNotContain("tenant-two-settlement", settlements, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-two-transfer", transfers, StringComparison.Ordinal);
        var settlementDetail = await tenantOne.GetStringAsync("/api/accounting/settlements/2001");
        var transferDetail = await tenantOne.GetStringAsync("/api/accounting/liquidity-transfers/3001");
        Assert.Contains("tenant-one-settlement", settlementDetail, StringComparison.Ordinal);
        Assert.Contains("tenant-one-transfer", transferDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-two-settlement", settlementDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-two-transfer", transferDetail, StringComparison.Ordinal);

        using var tenantTwo = Client(2, "accounting.settlements.view,accounting.liquidity_transfers.view");
        var otherSettlements = await tenantTwo.GetStringAsync("/api/accounting/settlements");
        var otherTransfers = await tenantTwo.GetStringAsync("/api/accounting/liquidity-transfers");
        Assert.Contains("tenant-two-settlement", otherSettlements, StringComparison.Ordinal);
        Assert.Contains("tenant-two-transfer", otherTransfers, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-one-settlement", otherSettlements, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-one-transfer", otherTransfers, StringComparison.Ordinal);

        using var tenantOneGet = Client(1, "accounting.settlements.view,accounting.liquidity_transfers.view");
        Assert.Equal(HttpStatusCode.NotFound, (await tenantOneGet.GetAsync("/api/accounting/settlements/2002")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantOneGet.GetAsync("/api/accounting/liquidity-transfers/3002")).StatusCode);
    }

    private HttpClient Client(long businessId, string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", "201");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        db.ChartAccounts.AddRange(Account(101, 1), Account(201, 2));
        db.BankAccounts.AddRange(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "One Bank" }, new BankAccount { Id = 502, BusinessId = 2, ChartAccountId = 201, DisplayName = "Two Bank" });
        db.SettlementBatches.AddRange(new SettlementBatch { Id = 2001, BusinessId = 1, SettlementType = "card", ClearingAccountId = 101, BankAccountId = 501, SettlementDate = DateTime.UtcNow, GrossClearingAmount = 10, NetBankAmount = 10, IdempotencyKey = "tenant-one-settlement", Status = SettlementBatchStatuses.Posted }, new SettlementBatch { Id = 2002, BusinessId = 2, SettlementType = "card", ClearingAccountId = 201, BankAccountId = 502, SettlementDate = DateTime.UtcNow, GrossClearingAmount = 20, NetBankAmount = 20, IdempotencyKey = "tenant-two-settlement", Status = SettlementBatchStatuses.Posted });
        db.SettlementBatches.Add(new SettlementBatch { Id = 2003, BusinessId = 1, SettlementType = "card", ClearingAccountId = 101, BankAccountId = 501, SettlementDate = DateTime.UtcNow, GrossClearingAmount = 5, NetBankAmount = 5, IdempotencyKey = "tenant-one-latest-settlement", Status = SettlementBatchStatuses.Posted });
        db.LiquidityTransfers.AddRange(new LiquidityTransfer { Id = 3001, BusinessId = 1, TransferType = LiquidityTransferTypes.CashToBank, SourceAccountId = 101, DestinationAccountId = 101, Amount = 10, PostingDate = DateTime.UtcNow, IdempotencyKey = "tenant-one-transfer", Status = LiquidityTransferStatuses.Posted }, new LiquidityTransfer { Id = 3002, BusinessId = 2, TransferType = LiquidityTransferTypes.CashToBank, SourceAccountId = 201, DestinationAccountId = 201, Amount = 20, PostingDate = DateTime.UtcNow, IdempotencyKey = "tenant-two-transfer", Status = LiquidityTransferStatuses.Posted });
        db.LiquidityTransfers.Add(new LiquidityTransfer { Id = 3003, BusinessId = 1, TransferType = LiquidityTransferTypes.CashToBank, SourceAccountId = 101, DestinationAccountId = 101, Amount = 5, PostingDate = DateTime.UtcNow, IdempotencyKey = "tenant-one-latest-transfer", Status = LiquidityTransferStatuses.Posted });
        await db.SaveChangesAsync();
    }

    private static ChartAccount Account(long id, long businessId) => new() { Id = id, BusinessId = businessId, AccountCode = id.ToString(), AccountName = $"Account {id}", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true };
}
