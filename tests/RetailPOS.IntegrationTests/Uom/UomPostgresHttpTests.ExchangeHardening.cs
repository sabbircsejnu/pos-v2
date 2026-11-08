using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Exchanges;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Exchanges;

namespace RetailPOS.Tests.Uom;

/// <summary>
/// Post-implementation hardening on real PostgreSQL: the terminal is revalidated at Confirm and Reverse (L6).
/// </summary>
public sealed partial class UomPostgresHttpTests
{
    private const long TerminalThree = 9133;

    private async Task SetTerminalActiveAsync(long terminalId, bool active)
    {
        await _fixture.WithDbAsync(async db =>
        {
            var terminal = await db.PosTerminals.SingleAsync(row => row.Id == terminalId);
            terminal.IsActive = active;
            await db.SaveChangesAsync();
        });
    }

    private async Task<int> ExchangeCountForSaleAsync(long saleId)
    {
        var count = 0;
        await _fixture.WithDbAsync(async db => count = await db.ExchangeSourceInvoices.AsNoTracking().CountAsync(row => row.SaleId == saleId));
        return count;
    }

    private static object WithTerminal(object exchange, long? terminalId)
    {
        var json = JsonSerializer.SerializeToElement(exchange);
        var map = json.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value);
        map["terminalId"] = terminalId;
        return map;
    }

    [Fact]
    public async Task ExchangeConfirm_RevalidatesTheTerminal_DeactivatedForeignOrChangedSincePreview_WithNoMutation()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "TMA", 100m);
        var jacket = await PricedVariantAsync(owner, "TMB", 120m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 2m, 100m);
        var body = ExchangeBody(saleId, itemId, 1m, jacket, 1m);
        object[] settlements = [new { direction = "collect", method = "cash", amount = 22m, tendered = 22m }];
        var shirtBefore = await OnHandAsync(shirt);
        var jacketBefore = await OnHandAsync(jacket);

        var preview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", body);
        var hash = preview.GetProperty("previewHash").GetString()!;

        // 1. Deactivated between Preview and Confirm: refused with an actionable message, no internal identifiers.
        await SetTerminalActiveAsync(UomPostgresFixture.Terminal, false);
        try
        {
            var (status, text) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, hash, Unique("ex"), settlements));
            Assert.True(status == HttpStatusCode.UnprocessableEntity, text);
            Assert.Contains("EXCHANGE_SESSION_REQUIRED", text);
            Assert.Contains("not active at this outlet", text);
            Assert.DoesNotContain(UomPostgresFixture.Terminal.ToString(System.Globalization.CultureInfo.InvariantCulture), text);
        }
        finally
        {
            await SetTerminalActiveAsync(UomPostgresFixture.Terminal, true);
        }

        // 2. A terminal of another outlet is refused.
        await EnsureOutletTwoTerminalAsync();
        var foreign = WithTerminal(body, UomPostgresFixture.TerminalTwo);
        Assert.Equal("EXCHANGE_SESSION_REQUIRED", await ErrorCodeAsync(owner, HttpMethod.Post, "/api/pos/exchanges",
            ConfirmBody(foreign, hash, Unique("ex"), settlements), HttpStatusCode.UnprocessableEntity));

        // 3. Another active terminal of the same outlet than the one reviewed: the preview is stale.
        await _fixture.WithDbAsync(async db =>
        {
            if (!await db.PosTerminals.AnyAsync(row => row.Id == TerminalThree))
                db.PosTerminals.Add(new PosTerminal { Id = TerminalThree, OutletId = UomPostgresFixture.Outlet, Name = "T3", Code = "T3", IsActive = false });
            await db.SaveChangesAsync();
        });
        await SetTerminalActiveAsync(TerminalThree, true);
        try
        {
            Assert.Equal("EXCHANGE_PREVIEW_STALE", await ErrorCodeAsync(owner, HttpMethod.Post, "/api/pos/exchanges",
                ConfirmBody(WithTerminal(body, TerminalThree), hash, Unique("ex"), settlements), HttpStatusCode.Conflict));

            // With two active terminals, an exchange without a terminal must name one.
            var unnamed = WithTerminal(body, null);
            var (status, text) = await PostRawAsync(owner, "/api/pos/exchanges/preview", unnamed);
            Assert.True(status == HttpStatusCode.UnprocessableEntity, text);
            Assert.Contains("more than one active terminal", text);
        }
        finally
        {
            await SetTerminalActiveAsync(TerminalThree, false);
        }

        Assert.Equal(0, await ExchangeCountForSaleAsync(saleId));
        Assert.Equal(shirtBefore, await OnHandAsync(shirt));
        Assert.Equal(jacketBefore, await OnHandAsync(jacket));

        // The reviewed terminal, active again, confirms the same preview.
        var (okStatus, okText) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, hash, Unique("ex"), settlements));
        Assert.True(okStatus == HttpStatusCode.Created, okText);
        Assert.Equal(1, await ExchangeCountForSaleAsync(saleId));
    }

    [Fact]
    public async Task ExchangeReverse_RefusesWhenTheOutletHasNoActiveTerminal_AndChangesNothing()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "TRA", 100m);
        var jacket = await PricedVariantAsync(owner, "TRB", 120m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 2m, 100m);
        var (exchangeId, _) = await ConfirmExchangeAsync(owner, ExchangeBody(saleId, itemId, 1m, jacket, 1m),
            [new { direction = "collect", method = "cash", amount = 22m, tendered = 22m }]);
        var jacketAfter = await OnHandAsync(jacket);

        await SetTerminalActiveAsync(UomPostgresFixture.Terminal, false);
        try
        {
            Assert.Equal("EXCHANGE_SESSION_REQUIRED", await ErrorCodeAsync(owner, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/reverse",
                ReverseBody(Unique("rv"), [new { direction = "refund", method = "cash", amount = 22m }]), HttpStatusCode.UnprocessableEntity));
        }
        finally
        {
            await SetTerminalActiveAsync(UomPostgresFixture.Terminal, true);
        }

        Assert.Equal(jacketAfter, await OnHandAsync(jacket));
        await _fixture.WithDbAsync(async db =>
            Assert.Equal(ExchangeStatusCompleted, await db.Exchanges.AsNoTracking().Where(row => row.Id == exchangeId).Select(row => row.Status).SingleAsync()));
    }

    private const string ExchangeStatusCompleted = "completed";

    private async Task EnsureOutletTwoTerminalAsync()
    {
        await _fixture.WithDbAsync(async db =>
        {
            if (!await db.PosTerminals.AnyAsync(row => row.Id == UomPostgresFixture.TerminalTwo))
            {
                db.PosTerminals.Add(new PosTerminal { Id = UomPostgresFixture.TerminalTwo, OutletId = UomPostgresFixture.OutletTwo, Name = "T2", Code = "T2", IsActive = true, IsDefault = true });
                await db.SaveChangesAsync();
            }
        });
    }

    [Fact]
    public async Task ExchangeApproval_LocksTheSupervisorLoginAcrossCashiers_UnknownLoginsAlike_AndTheCashier_WithoutStoringLogins()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner, window: 7);
        var shirt = await PricedVariantAsync(owner, "LKA", 100m);
        var jacket = await PricedVariantAsync(owner, "LKB", 100m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);
        await _fixture.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sales SET sale_date = sale_date - interval '9 days' WHERE id = {saleId}"));

        const string password = "Supervisor#123";
        var supervisorEmail = $"{Unique("lk")}@example.test";
        await _fixture.WithDbAsync(async db =>
        {
            var role = new Role { Name = $"ExchangeSupervisor{Guid.NewGuid():N}"[..30], Permissions = "[\"pos.exchange.override_window\"]" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            var supervisor = new User { BusinessId = UomPostgresFixture.Business, RoleId = role.Id, Name = "Supervisor", Email = supervisorEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 4), IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                OutletId = UomPostgresFixture.Outlet };
            db.Users.Add(supervisor);
            await db.SaveChangesAsync();
            db.UserOutletAssignments.Add(new UserOutletAssignment { UserId = supervisor.Id, OutletId = UomPostgresFixture.Outlet, BusinessId = UomPostgresFixture.Business, IsPrimary = true, IsActive = true });
            await db.SaveChangesAsync();
        });

        var body = ExchangeBody(saleId, itemId, 1m, jacket, 1m);
        var preview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", body);
        var hash = preview.GetProperty("previewHash").GetString()!;
        object Approval(string login, string secret) => new
        {
            preview = body, previewHash = hash, approvals = new[] { new { type = "window", reason = "Customer was travelling" } },
            supervisorLogin = login, supervisorPassword = secret
        };
        // Each cashier works from its own address, so the per-IP rate limit (the per-source control) is not what stops it.
        var source = 0;
        HttpClient FromOwnAddress(HttpClient client)
        {
            client.DefaultRequestHeaders.Add("X-Test-RemoteIp", $"10.77.{Random.Shared.Next(0, 250)}.{++source}");
            return client;
        }
        async Task<HttpClient> CashierAsync() => FromOwnAddress(_fixture.Client(await NewOutletCashierAsync(), "BusinessOwner", "*"));
        async Task<(HttpStatusCode Status, string Code, string Message)> ApproveAsync(HttpClient client, string login, string secret)
        {
            var (status, text) = await PostRawAsync(client, "/api/pos/exchanges/approvals", Approval(login, secret));
            using var json = JsonDocument.Parse(text);
            return (status,
                json.RootElement.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString()! : string.Empty,
                json.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString()! : string.Empty);
        }

        // 1. Five wrong passwords from two cashiers (each below its own rate limit) lock the supervisor login, even for a
        //    third cashier with the right password and the login typed in another case.
        var first = await CashierAsync();
        var second = await CashierAsync();
        foreach (var client in new[] { first, first, first, second, second })
        {
            var failed = await ApproveAsync(client, supervisorEmail, "wrong-password");
            Assert.Equal((HttpStatusCode.Forbidden, ExchangeErrorCodes.ApprovalInvalid), (failed.Status, failed.Code));
        }
        var locked = await ApproveAsync(await CashierAsync(), supervisorEmail.ToUpperInvariant(), password);
        Assert.Equal((HttpStatusCode.TooManyRequests, ExchangeErrorCodes.ApprovalLocked), (locked.Status, locked.Code));

        // 2. An unknown login fails and locks exactly the same way, with the same answers.
        var unknown = $"{Unique("nobody")}@example.test";
        var third = await CashierAsync();
        var fourth = await CashierAsync();
        foreach (var client in new[] { third, third, third, fourth, fourth })
        {
            var failed = await ApproveAsync(client, unknown, "wrong-password");
            Assert.Equal((HttpStatusCode.Forbidden, ExchangeErrorCodes.ApprovalInvalid), (failed.Status, failed.Code));
        }
        var unknownLocked = await ApproveAsync(await CashierAsync(), unknown, "whatever");
        Assert.Equal((locked.Status, locked.Code, locked.Message), (unknownLocked.Status, unknownLocked.Code, unknownLocked.Message));

        // 3. A cashier with ten recent failures is locked whatever supervisor it names.
        var cashierId = await NewOutletCashierAsync();
        await _fixture.WithDbAsync(async db =>
        {
            var key = GrantExchangeApprovalsCommandHandler.CashierKey(UomPostgresFixture.Business, cashierId);
            db.ExchangeApprovalFailures.AddRange(Enumerable.Range(0, GrantExchangeApprovalsCommandHandler.CashierFailureLimit).Select(_ => new ExchangeApprovalFailure
            {
                BusinessId = UomPostgresFixture.Business, SubjectKind = GrantExchangeApprovalsCommandHandler.CashierSubject, SubjectHash = key, AttemptedAtUtc = DateTime.UtcNow
            }));
            await db.SaveChangesAsync();
        });
        var cashierLocked = await ApproveAsync(FromOwnAddress(_fixture.Client(cashierId, "BusinessOwner", "*")), supervisorEmail, password);
        Assert.Equal((HttpStatusCode.TooManyRequests, ExchangeErrorCodes.ApprovalLocked), (cashierLocked.Status, cashierLocked.Code));

        // 4. Once the window has passed, the supervisor signs in, and success clears the login's failures.
        var supervisorKey = GrantExchangeApprovalsCommandHandler.SupervisorKey(UomPostgresFixture.Business, supervisorEmail);
        await _fixture.WithDbAsync(db => db.ExchangeApprovalFailures
            .Where(row => row.SubjectHash == supervisorKey)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.AttemptedAtUtc, DateTime.UtcNow.AddMinutes(-16))));
        var fifth = await CashierAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(fifth, supervisorEmail, "wrong-password")).Status);
        var granted = await ApproveAsync(fifth, supervisorEmail, password);
        Assert.True(granted.Status is HttpStatusCode.OK or HttpStatusCode.Created, granted.Message);
        await _fixture.WithDbAsync(async db =>
        {
            Assert.False(await db.ExchangeApprovalFailures.AnyAsync(row => row.SubjectHash == supervisorKey));

            // Only hashes are stored: no login, e-mail or user id in clear.
            var stored = await db.ExchangeApprovalFailures.AsNoTracking().Select(row => row.SubjectHash).ToListAsync();
            Assert.NotEmpty(stored);
            Assert.All(stored, value => Assert.Matches("^[0-9a-f]{64}$", value));
        });
    }
}
