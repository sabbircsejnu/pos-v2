using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RetailPOS.API.Services;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Integration;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Integration;
using RetailPOS.Infrastructure.Pos;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.Tests.Sales;

namespace RetailPOS.IntegrationTests.Phase3;

/// <summary>
/// Phase 3 on a real PostgreSQL schema built from the migration chain (isolated Test stack only): the sale-versus-close
/// serialisation, exactly-once cash facts, the database triggers, UTC/BusinessDate storage and outbox atomicity.
/// No session validator, store or lock is mocked.
/// </summary>
public sealed class CashSessionPostgresTests
{
    private const long OutletId = 5001;
    private const long Terminal1 = 8001;
    private const long Terminal2 = 8002;
    private const long Cashier = 7001;
    private const long Manager = 7002;
    private const long Reviewer = 7003;

    static CashSessionPostgresTests() =>
        // The same legacy mode the other PostgreSQL suites run in: timestamptz may then be read as Local-kind values.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    private static Task RunAsync(Func<PostgresTestDatabase, Task> operation) =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.SaleCreation,
            async database =>
            {
                await SeedAsync(database);
                await operation(database);
            });

    private static RetailPOSDbContext Ctx(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

    private static async Task SeedAsync(PostgresTestDatabase database)
    {
        await using var db = Ctx(database);
        await db.Database.MigrateAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Phase 3 business", IsActive = true });
        db.Businesses.Add(new Business { Id = 2, Name = "Other business", IsActive = true });
        await db.SaveChangesAsync();
        db.Outlets.Add(new Outlet { Id = OutletId, BusinessId = 1, Name = "Main outlet", Address = "Dhaka", InvoicePrefix = "DHK" });
        db.Outlets.Add(new Outlet { Id = 5002, BusinessId = 2, Name = "Foreign outlet", Address = "X", InvoicePrefix = "FOR" });
        foreach (var (id, name) in new[] { (Cashier, "Cashier"), (Manager, "Manager"), (Reviewer, "Reviewer") })
            db.Users.Add(new User
            {
                Id = id, BusinessId = 1, Name = name, Email = $"{name.ToLowerInvariant()}@phase3.test", PasswordHash = "x",
                OutletId = OutletId, IsActive = true
            });
        db.Customers.Add(new Customer
        {
            Id = 6001, BusinessId = 1, Name = Customer.WalkInCustomerName, CustomerCode = Customer.WalkInCustomerCode,
            IsSystem = true, IsActive = true
        });
        db.Categories.Add(new Category { Id = 3001, BusinessId = 1, Name = "General" });
        db.PosTerminals.Add(new PosTerminal { Id = Terminal1, OutletId = OutletId, Name = "Counter 1", Code = "T-1", IsActive = true, IsDefault = true });
        db.PosTerminals.Add(new PosTerminal { Id = Terminal2, OutletId = OutletId, Name = "Counter 2", Code = "T-2", IsActive = true });
        for (var i = 1; i <= 12; i++)
        {
            db.Products.Add(new Product
            {
                Id = 3100 + i, BusinessId = 1, Name = $"Product {i}", ProductCode = $"P-{i}", CategoryId = 3001,
                BasePrice = 100m, CostPrice = 70m, Status = ProductStatus.Active, HasVariants = true
            });
            db.ProductVariants.Add(new ProductVariant
            {
                Id = 3200 + i, BusinessId = 1, ProductId = 3100 + i, Name = "Default", Sku = $"SKU-{i}", Barcode = $"BC-{i}", Attributes = "{}"
            });
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 9000 + i, VariantId = 3200 + i, LocationType = "outlet", LocationId = OutletId, Quantity = 100, LowStockThreshold = 1
            });
        }

        await db.SaveChangesAsync();
    }

    // ── handler builders over independent DbContexts (each simulates a separate request) ─────────────────────────────

    private static IBusinessPosSettingsStore Settings(int hours = 20, string zone = "Asia/Dhaka")
    {
        var settings = new Mock<IBusinessPosSettingsStore>();
        settings.Setup(s => s.GetAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessPosSettingsSnapshot(1, hours, zone, true, 0m, "VAT", false, 100m, 1, null, null, true));
        return settings.Object;
    }

    private static ICashSessionWriteOutletAuthorizer WriteAuthorizer()
    {
        var authorizer = new Mock<ICashSessionWriteOutletAuthorizer>();
        authorizer.Setup(a => a.AuthorizeWriteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long outlet, CancellationToken _) => outlet);
        return authorizer.Object;
    }

    private static ICashSessionEventWriter Events(RetailPOSDbContext db) =>
        new CashSessionEventWriter(new IntegrationOutbox(
            db, Mock.Of<IAuditContext>(), TimeProvider.System, Mock.Of<IIntegrationReplayAuthorization>()));

    private static async Task<CashSessionReadModel> OpenAsync(
        PostgresTestDatabase database,
        long terminalId = Terminal1,
        int hours = 20,
        decimal opening = 100m,
        TimeProvider? clock = null,
        string zone = "Asia/Dhaka",
        string? key = null)
    {
        await using var db = Ctx(database);
        var handler = new OpenCashSessionCommandHandler(
            new CashSessionStore(db), WriteAuthorizer(), Settings(hours, zone), TestCompanySettings.Provider(zone), Mock.Of<ICashSessionOpenAuditWriter>(),
            Mock.Of<ICashSessionOpenedPublisher>(), Events(db), clock ?? TimeProvider.System);
        return await handler.Handle(new OpenCashSessionCommand(OutletId, terminalId, opening, null, Cashier, key), CancellationToken.None);
    }

    private static CloseCashSessionCommandHandler CloseHandler(RetailPOSDbContext db, TimeProvider? clock = null) =>
        new(new CashSessionStore(db), WriteAuthorizer(), Mock.Of<ICashSessionCloseAuditWriter>(),
            Mock.Of<ICashSessionClosedPublisher>(), Events(db), clock ?? TimeProvider.System);

    private static async Task<CashSessionReadModel> CloseAsync(
        PostgresTestDatabase database, long sessionId, decimal counted, string? notes = null, long user = Manager)
    {
        await using var db = Ctx(database);
        return await CloseHandler(db).Handle(
            new CloseCashSessionCommand(sessionId, counted, notes, user), CancellationToken.None);
    }

    private sealed class PausingValidator(ISaleCashSessionValidator inner, Func<Task> afterLock) : ISaleCashSessionValidator
    {
        public async Task<CashSession> ValidateActiveForSaleAsync(long outletId, long terminalId, long? actor, CancellationToken ct)
        {
            var session = await inner.ValidateActiveForSaleAsync(outletId, terminalId, actor, ct);
            await afterLock();
            return session;
        }

        public Task ValidateFinalGateAsync(CashSession session, CancellationToken ct) => inner.ValidateFinalGateAsync(session, ct);
    }

    private static async Task<RetailPOS.Application.Sales.ReadModels.SaleReadModel> SellAsync(
        PostgresTestDatabase database,
        string key,
        long terminalId = Terminal1,
        long variantId = 3201,
        TimeProvider? clock = null,
        Func<Task>? afterSessionLock = null,
        decimal tendered = 150m)
    {
        await using var db = Ctx(database);
        ISaleCashSessionValidator validator = new SaleCashSessionValidator(
            new CashSessionStore(db), Mock.Of<IAuditService>(), Mock.Of<ICashSessionExpiryPublisher>(),
            clock ?? TimeProvider.System, NullLogger<SaleCashSessionValidator>.Instance);
        if (afterSessionLock is not null)
            validator = new PausingValidator(validator, afterSessionLock);

        var cache = new Mock<IPosCacheService>();
        cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
        var events = new Mock<ISaleEventPublisher>();
        var handler = SaleCreationCommandTestFactory.Create(
            db, validator, new SaleInvoiceNumberAllocator(db), Mock.Of<IAuditService>(),
            new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db, NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance),
            cache.Object, events.Object);

        return await handler.Handle(
            new CreateSaleCommand(
                OutletId, terminalId, null, [new CreateSaleLine(variantId, 1, 100m, 0m, null, null)], 0m, 0m, "cash", [],
                Cashier, null, null, key, tendered),
            CancellationToken.None);
    }

    private static async Task<T> ScalarAsync<T>(PostgresTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private static async Task<string?> ExecuteExpectingErrorAsync(PostgresTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        try
        {
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.MessageText;
        }
    }

    // ── opening, BusinessDate and UTC storage ────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task Open_persists_server_derived_date_instants_and_snapshots_with_a_durable_outbox_event() => RunAsync(async database =>
    {
        var now = new DateTime(2026, 11, 3, 18, 30, 0, DateTimeKind.Utc); // 4 Nov, 00:30 in Dhaka
        var opened = await OpenAsync(database, hours: 12, clock: new FixedClock(now));

        Assert.Equal(new DateTime(2026, 11, 4), opened.BusinessDate);
        Assert.Equal("date", await ScalarAsync<string>(database,
            "SELECT data_type FROM information_schema.columns WHERE table_name='cash_sessions' AND column_name='business_date'"));
        Assert.Equal("2026-11-04", await ScalarAsync<string>(database, $"SELECT business_date::text FROM cash_sessions WHERE id = {opened.Id}"));
        Assert.Equal("Asia/Dhaka", await ScalarAsync<string>(database, $"SELECT time_zone_id_snapshot FROM cash_sessions WHERE id = {opened.Id}"));
        Assert.Equal(12, await ScalarAsync<int>(database, $"SELECT duration_snapshot_hours FROM cash_sessions WHERE id = {opened.Id}"));
        Assert.Equal("timestamp with time zone", await ScalarAsync<string>(database,
            "SELECT data_type FROM information_schema.columns WHERE table_name='cash_sessions' AND column_name='opened_at'"));

        await using var db = Ctx(database);
        var stored = await db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == opened.Id);
        Assert.Equal(now, UtcTime.Normalize(stored.OpenedAt));
        Assert.Equal(now.AddHours(12), stored.CutoffAtUtc);
        var message = await db.Set<IntegrationOutboxMessage>().AsNoTracking().SingleAsync(m => m.AggregateId == opened.Id.ToString());
        Assert.Equal(CashSessionEventTypes.Opened, message.MessageType);
        Assert.Equal(1, message.BusinessId);
    });

    [Fact]
    public Task Open_is_idempotent_and_the_database_allows_one_open_session_per_terminal() => RunAsync(async database =>
    {
        var first = await OpenAsync(database, key: "k-1");
        var replay = await OpenAsync(database, key: "k-1");
        Assert.Equal(first.Id, replay.Id);

        var error = await Assert.ThrowsAsync<CashSessionException>(() => OpenAsync(database));
        Assert.Equal(CashSessionErrorCodes.AlreadyOpen, error.Code);

        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_sessions"));
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM integration_outbox_messages"));
    });

    [Fact]
    public Task Two_concurrent_opens_of_one_terminal_create_exactly_one_session_and_one_event() => RunAsync(async database =>
    {
        var results = await Task.WhenAll(
            Task.Run(async () => { try { return (await OpenAsync(database), (Exception?)null); } catch (Exception e) { return (null, e); } }),
            Task.Run(async () => { try { return (await OpenAsync(database), (Exception?)null); } catch (Exception e) { return (null, e); } }));

        Assert.Single(results, r => r.Item1 is not null);
        var failure = Assert.Single(results, r => r.Item2 is not null).Item2;
        Assert.Equal(CashSessionErrorCodes.AlreadyOpen, Assert.IsType<CashSessionException>(failure).Code);
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_sessions WHERE status='open'"));
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM integration_outbox_messages"));
    });

    [Fact]
    public Task Business_date_is_independent_of_the_host_zone_in_utc_dhaka_and_a_dst_zone() => RunAsync(async database =>
    {
        // 2026-11-01 04:30Z: Dhaka 10:30 on 1 Nov, New York 00:30 EDT on 1 Nov (before the fall-back at 06:00Z), UTC 1 Nov.
        var instant = new DateTime(2026, 11, 1, 4, 30, 0, DateTimeKind.Utc);
        var utc = await OpenAsync(database, Terminal1, clock: new FixedClock(instant), zone: "UTC");
        var dhaka = await OpenAsync(database, Terminal2, clock: new FixedClock(instant), zone: "Asia/Dhaka");
        await CloseAsync(database, utc.Id, 100m);
        var ny = await OpenAsync(database, Terminal1, clock: new FixedClock(instant.AddMinutes(1)), zone: "America/New_York");

        Assert.Equal(new DateTime(2026, 11, 1), utc.BusinessDate);
        Assert.Equal(new DateTime(2026, 11, 1), dhaka.BusinessDate);
        Assert.Equal(new DateTime(2026, 11, 1), ny.BusinessDate);
        Assert.Equal("America/New_York", ny.TimeZoneId);
        // Persisted instants are timestamptz in UTC regardless of how Npgsql materialises them.
        Assert.Equal("2026-11-01 04:30:00", await ScalarAsync<string>(database,
            $"SELECT to_char(opened_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS') FROM cash_sessions WHERE id = {dhaka.Id}"));
    });

    // ── sale versus close ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task A_sale_in_the_session_is_part_of_the_authoritative_close_summary() => RunAsync(async database =>
    {
        var session = await OpenAsync(database, opening: 100m);
        await SellAsync(database, "s-1");
        await SellAsync(database, "s-2", variantId: 3202);

        var closed = await CloseAsync(database, session.Id, 300m);

        Assert.Equal(300m, closed.ExpectedClosingCashAmount); // 100 opening + 2 x 100 retained; change 50 each excluded
        Assert.Equal(0m, closed.DifferenceAmount);
        Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_difference_approvals"));
        Assert.Equal(200m, await ScalarAsync<decimal>(database,
            $"SELECT cash_sales_retained FROM cash_session_close_snapshots WHERE cash_session_id = {session.Id}"));
        Assert.Equal(100m, await ScalarAsync<decimal>(database,
            $"SELECT change_given FROM cash_session_close_snapshots WHERE cash_session_id = {session.Id}"));
    });

    [Fact]
    public Task Close_holding_its_lock_makes_a_concurrent_sale_wait_and_then_fail_as_closed() => RunAsync(async database =>
    {
        var session = await OpenAsync(database);
        var closeLockHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // A close that has taken FOR UPDATE and has not committed yet.
        var closing = Task.Run(async () =>
        {
            await using var db = Ctx(database);
            var store = new CashSessionStore(db);
            await using var tx = await store.BeginTransactionAsync(CancellationToken.None);
            await store.LockAsync(session.Id, CashSessionLockMode.Update, CancellationToken.None);
            closeLockHeld.SetResult();
            await releaseClose.Task;
            await store.MarkClosedAsync(new CashSessionClosePersist(session.Id, 100m, 100m, 0m, DateTime.UtcNow, Manager, null, null), CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        });
        await closeLockHeld.Task;

        var sale = Task.Run(() => SellAsync(database, "blocked-sale"));
        await Task.Delay(1500);
        Assert.False(sale.IsCompleted, "the sale must wait for the closing transaction instead of racing it");

        releaseClose.SetResult();
        await closing;
        var error = await Assert.ThrowsAsync<CashSessionException>(() => sale);

        Assert.Equal(CashSessionErrorCodes.Closed, error.Code);
        Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM sales"));
        Assert.Equal(100L, await ScalarAsync<long>(database, "SELECT quantity FROM inventories WHERE variant_id = 3201"));
    });

    [Fact]
    public Task A_sale_holding_the_shared_lock_makes_close_wait_and_the_sale_is_in_the_summary() => RunAsync(async database =>
    {
        var session = await OpenAsync(database, opening: 100m);
        var lockTaken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var sale = Task.Run(() => SellAsync(database, "slow-sale", afterSessionLock: async () =>
        {
            lockTaken.SetResult();
            await releaseSale.Task;
        }));
        await lockTaken.Task;

        var close = Task.Run(() => CloseAsync(database, session.Id, 200m));
        await Task.Delay(1500);
        Assert.False(close.IsCompleted, "close must wait for the in-flight sale (FOR SHARE conflicts with FOR UPDATE)");

        releaseSale.SetResult();
        await sale;
        var closed = await close;

        Assert.Equal(200m, closed.ExpectedClosingCashAmount); // the committed sale is inside the authoritative summary
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM sales"));
    });

    [Fact]
    public Task Concurrent_sales_and_a_close_never_leave_a_committed_sale_outside_the_closing_summary() => RunAsync(async database =>
    {
        for (var round = 0; round < 3; round++)
        {
            var session = await OpenAsync(database, round % 2 == 0 ? Terminal1 : Terminal2, opening: 50m);
            var terminal = session.TerminalId;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var sales = Enumerable.Range(1, 8).Select(i => Task.Run(async () =>
            {
                await start.Task;
                try { await SellAsync(database, $"r{round}-s{i}", terminal, 3200 + i); return (Exception?)null; }
                catch (Exception exception) { return exception; }
            })).ToList();
            var close = Task.Run(async () =>
            {
                await start.Task;
                await Task.Delay(Random.Shared.Next(5, 120));
                return await CloseAsync(database, session.Id, 0m, "stress close");
            });
            start.SetResult();
            var errors = (await Task.WhenAll(sales)).Where(e => e is not null).ToList();
            await close;

            // Only an explicit closed/no-session refusal is acceptable for a sale that lost the race.
            Assert.All(errors, e => Assert.True(
                e is CashSessionException { Code: CashSessionErrorCodes.Closed or CashSessionErrorCodes.NoActiveSession },
                $"unexpected failure: {e}"));

            var committedCash = await ScalarAsync<decimal>(database, $@"
                SELECT COALESCE(SUM(p.net_cash_retained), 0)
                FROM sales s JOIN sale_payments p ON p.sale_id = s.id
                WHERE s.cash_session_id = {session.Id}");
            var snapshotCash = await ScalarAsync<decimal>(database,
                $"SELECT cash_sales_retained FROM cash_session_close_snapshots WHERE cash_session_id = {session.Id}");
            Assert.Equal(committedCash, snapshotCash);
            Assert.Equal(8 - errors.Count, (int)await ScalarAsync<long>(database, $"SELECT count(*) FROM sales WHERE cash_session_id = {session.Id}"));
        }
    });

    [Fact]
    public Task Two_concurrent_closes_produce_one_financial_close_and_identical_results() => RunAsync(async database =>
    {
        var session = await OpenAsync(database, opening: 100m);
        await SellAsync(database, "sale-before-close");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var closes = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await CloseAsync(database, session.Id, 190m, "short 10");
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(closes);

        Assert.All(results, r => Assert.Equal("closed", r.State));
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_session_close_snapshots"));
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_difference_approvals"));
        Assert.Equal(1L, await ScalarAsync<long>(database,
            $"SELECT count(*) FROM integration_outbox_messages WHERE message_type = '{CashSessionEventTypes.Closed}'"));
        Assert.Equal(1L, await ScalarAsync<long>(database,
            $"SELECT count(*) FROM integration_outbox_messages WHERE message_type = '{CashSessionEventTypes.VarianceRaised}'"));
    });

    [Fact]
    public Task A_sequential_close_retry_returns_the_existing_result_without_new_facts() => RunAsync(async database =>
    {
        var session = await OpenAsync(database);
        var first = await CloseAsync(database, session.Id, 90m, "short");
        var retry = await CloseAsync(database, session.Id, 5m, "different retry", user: Reviewer);

        Assert.Equal(first.ClosedAt, retry.ClosedAt);
        Assert.Equal(first.ClosedByUserId, retry.ClosedByUserId);
        Assert.Equal(90m, retry.ActualClosingCashAmount);
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_session_close_snapshots"));
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_difference_approvals"));
    });

    // ── cutoff ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task Sale_at_the_exact_cutoff_is_rejected_for_everyone_but_the_session_stays_closable() => RunAsync(async database =>
    {
        var opened = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        var session = await OpenAsync(database, hours: 2, clock: new FixedClock(opened));
        var cutoff = opened.AddHours(2);

        await SellAsync(database, "before", clock: new FixedClock(cutoff.AddTicks(-10)));
        var overdue = await Assert.ThrowsAsync<CashSessionException>(() =>
            SellAsync(database, "at-cutoff", variantId: 3202, clock: new FixedClock(cutoff)));
        Assert.Equal(CashSessionErrorCodes.Overdue, overdue.Code);
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM sales"));

        await using var db = Ctx(database);
        var closed = await CloseHandler(db, new FixedClock(cutoff.AddHours(3))).Handle(
            new CloseCashSessionCommand(session.Id, 200m, null, Manager), CancellationToken.None);
        Assert.Equal("closed", closed.State);
        Assert.True(await ScalarAsync<bool>(database, $"SELECT was_overdue_at_close FROM cash_session_close_snapshots WHERE cash_session_id = {session.Id}"));
    });

    [Fact]
    public Task The_final_gate_before_commit_rolls_back_a_sale_whose_cutoff_passed_during_processing() => RunAsync(async database =>
    {
        var opened = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        await OpenAsync(database, hours: 1, clock: new FixedClock(opened));
        // The first reading (lock-time validation) is just before the cutoff; every later reading is past it.
        var clock = new SteppingClock(opened.AddHours(1).AddTicks(-1), opened.AddHours(1).AddMinutes(1));

        var error = await Assert.ThrowsAsync<CashSessionException>(() => SellAsync(database, "late", clock: clock));

        Assert.Equal(CashSessionErrorCodes.Overdue, error.Code);
        Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM sales"));
        Assert.Equal(100L, await ScalarAsync<long>(database, "SELECT quantity FROM inventories WHERE variant_id = 3201"));
        Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM sale_invoice_counters"));
    });

    [Fact]
    public Task A_new_session_cannot_open_on_the_terminal_until_the_overdue_one_is_closed() => RunAsync(async database =>
    {
        var opened = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        var session = await OpenAsync(database, hours: 1, clock: new FixedClock(opened));

        var blocked = await Assert.ThrowsAsync<CashSessionException>(() =>
            OpenAsync(database, clock: new FixedClock(opened.AddHours(5))));
        Assert.Equal(CashSessionErrorCodes.AlreadyOpen, blocked.Code);

        await CloseAsync(database, session.Id, 100m);
        var next = await OpenAsync(database, clock: new FixedClock(opened.AddHours(5)));
        Assert.NotEqual(session.Id, next.Id);
    });

    // ── database guards ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task Triggers_refuse_sales_and_cash_movements_in_a_closed_session_and_protect_the_facts() => RunAsync(async database =>
    {
        var session = await OpenAsync(database);
        await CloseAsync(database, session.Id, 100m);

        Assert.Contains("CASH_SESSION_CLOSED", await ExecuteExpectingErrorAsync(database, $@"
            INSERT INTO cash_session_movements (business_id, outlet_id, cash_session_id, movement_type, cash_effect, source_type, source_key, recorded_at_utc, recorded_by_user_id)
            VALUES (1, {OutletId}, {session.Id}, 'cash_refund', -5, 'test', 'late-refund', now(), {Manager})") ?? "");
        Assert.Contains("CASH_SESSION_CLOSED", await ExecuteExpectingErrorAsync(database, $@"
            INSERT INTO sales (sale_number, outlet_id, cash_session_id, total_amount, discount, tax, payment_method, status, cashier_id, sale_date, created_at, business_id)
            VALUES ('X1', {OutletId}, {session.Id}, 1, 0, 0, 'cash', 'completed', {Cashier}, now(), now(), 1)") ?? "");
        Assert.Contains("CASH_SESSION_FACT_IMMUTABLE", await ExecuteExpectingErrorAsync(database,
            $"UPDATE cash_session_close_snapshots SET counted_cash = 999 WHERE cash_session_id = {session.Id}") ?? "");
        Assert.Contains("CASH_SESSION_FACT_IMMUTABLE", await ExecuteExpectingErrorAsync(database,
            $"DELETE FROM cash_session_close_snapshots WHERE cash_session_id = {session.Id}") ?? "");
    });

    [Fact]
    public Task Cash_movements_are_append_only() => RunAsync(async database =>
    {
        var session = await OpenAsync(database);
        await RecordAsync(database, session.Id, "append-1", 5m, CashSessionMovementTypes.Correction);

        Assert.Contains("CASH_SESSION_FACT_IMMUTABLE", await ExecuteExpectingErrorAsync(database, "UPDATE cash_session_movements SET cash_effect = 50") ?? "");
        Assert.Contains("CASH_SESSION_FACT_IMMUTABLE", await ExecuteExpectingErrorAsync(database, "DELETE FROM cash_session_movements") ?? "");
    });

    // ── cash movements: exactly once, versus close, rollback ─────────────────────────────────────────────────────────

    private static async Task<CashSessionMovementResult> RecordAsync(
        PostgresTestDatabase database, long sessionId, string sourceKey, decimal effect = -10m,
        string type = CashSessionMovementTypes.DrawerExpense, Func<Task>? beforeCommit = null, bool commit = true)
    {
        await using var db = Ctx(database);
        var store = new CashSessionStore(db);
        await using var tx = await store.BeginTransactionAsync(CancellationToken.None);
        var result = await new CashSessionMovementRecorder(db, store, Events(db), TimeProvider.System).RecordAsync(
            new CashSessionMovementRequest(sessionId, type, effect, "test", 1, sourceKey, null, null, null, Manager),
            CancellationToken.None);
        if (beforeCommit is not null)
            await beforeCommit();
        if (commit)
            await tx.CommitAsync(CancellationToken.None);
        else
            await tx.RollbackAsync(CancellationToken.None);
        return result;
    }

    [Fact]
    public Task Concurrent_retries_of_one_cash_effect_commit_exactly_once() => RunAsync(async database =>
    {
        var session = await OpenAsync(database);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await RecordAsync(database, session.Id, "expense:1:payout");
        })).Append(Task.Run(async () =>
        {
            start.SetResult();
            return await RecordAsync(database, session.Id, "expense:1:payout");
        })));

        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_session_movements"));
        Assert.Equal(1L, await ScalarAsync<long>(database,
            $"SELECT count(*) FROM integration_outbox_messages WHERE message_type = '{CashSessionEventTypes.MovementRecorded}'"));
    });

    [Fact]
    public Task A_cash_movement_waits_for_a_closing_session_and_is_refused_afterwards() => RunAsync(async database =>
    {
        var session = await OpenAsync(database);
        var closeLockHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = Task.Run(async () =>
        {
            await using var db = Ctx(database);
            var store = new CashSessionStore(db);
            await using var tx = await store.BeginTransactionAsync(CancellationToken.None);
            await store.LockAsync(session.Id, CashSessionLockMode.Update, CancellationToken.None);
            closeLockHeld.SetResult();
            await releaseClose.Task;
            await store.MarkClosedAsync(new CashSessionClosePersist(session.Id, 100m, 100m, 0m, DateTime.UtcNow, Manager, null, null), CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        });
        await closeLockHeld.Task;

        var payout = Task.Run(() => RecordAsync(database, session.Id, "refund:late", -25m, CashSessionMovementTypes.CashRefund));
        await Task.Delay(1500);
        Assert.False(payout.IsCompleted);
        releaseClose.SetResult();
        await closing;

        var error = await Assert.ThrowsAsync<CashSessionException>(() => payout);
        Assert.Equal(CashSessionErrorCodes.Closed, error.Code);
        Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_session_movements"));
    });

    [Fact]
    public Task A_movement_holding_the_shared_lock_is_inside_the_summary_of_the_waiting_close() => RunAsync(async database =>
    {
        var session = await OpenAsync(database, opening: 100m);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var payout = Task.Run(() => RecordAsync(database, session.Id, "expense:9:payout", -30m, beforeCommit: async () =>
        {
            held.SetResult();
            await release.Task;
        }));
        await held.Task;

        var close = Task.Run(() => CloseAsync(database, session.Id, 70m));
        await Task.Delay(1500);
        Assert.False(close.IsCompleted);
        release.SetResult();
        await payout;

        var closed = await close;
        Assert.Equal(70m, closed.ExpectedClosingCashAmount);
        Assert.Equal(0m, closed.DifferenceAmount);
    });

    [Fact]
    public Task A_rolled_back_cash_movement_leaves_no_fact_and_no_event() => RunAsync(async database =>
    {
        var session = await OpenAsync(database);
        var outboxBefore = await ScalarAsync<long>(database, "SELECT count(*) FROM integration_outbox_messages");

        await RecordAsync(database, session.Id, "expense:5:payout", commit: false);

        Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM cash_session_movements"));
        Assert.Equal(outboxBefore, await ScalarAsync<long>(database, "SELECT count(*) FROM integration_outbox_messages"));
        // The key was never consumed, so the effect can still be recorded exactly once.
        Assert.True((await RecordAsync(database, session.Id, "expense:5:payout")).Created);
    });

    // ── reconciliation across sessions: D1 ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task A_later_session_cash_refund_is_the_dispensing_sessions_outflow_and_never_rewrites_the_original() => RunAsync(async database =>
    {
        var original = await OpenAsync(database, Terminal1, opening: 100m);
        var sale = await SellAsync(database, "original-sale"); // +100 retained in session 1
        var closedOriginal = await CloseAsync(database, original.Id, 200m);
        Assert.Equal(200m, closedOriginal.ExpectedClosingCashAmount);
        var snapshotHashBefore = await ScalarAsync<string>(database, $"SELECT snapshot_hash FROM cash_session_close_snapshots WHERE cash_session_id = {original.Id}");

        var dispensing = await OpenAsync(database, Terminal2, opening: 500m);
        await using (var db = Ctx(database))
        {
            var store = new CashSessionStore(db);
            var recorder = new SaleCashRefundRecorder(db, store, new CashSessionMovementRecorder(db, store, Events(db), TimeProvider.System));
            await using var tx = await store.BeginTransactionAsync(CancellationToken.None);
            var saleEntity = await db.Sales.Include(s => s.Payments).SingleAsync(s => s.Id == sale.Id);
            var refund = await recorder.RecordAsync(
                new SaleCashRefundRequest(saleEntity, SaleCashRefundKind.Void, Manager, Terminal2, true, null, "customer returned"),
                CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
            Assert.Equal(dispensing.Id, refund!.DispensingCashSessionId);
            Assert.Equal(100m, refund.Amount);
        }

        var closedDispensing = await CloseAsync(database, dispensing.Id, 400m);
        Assert.Equal(400m, closedDispensing.ExpectedClosingCashAmount); // 500 opening - 100 refund paid from this drawer
        Assert.Equal(snapshotHashBefore, await ScalarAsync<string>(database, $"SELECT snapshot_hash FROM cash_session_close_snapshots WHERE cash_session_id = {original.Id}"));
        Assert.Equal(200m, await ScalarAsync<decimal>(database, $"SELECT expected_cash FROM cash_session_close_snapshots WHERE cash_session_id = {original.Id}"));
    });

    [Fact]
    public Task A_cash_refund_without_an_eligible_open_session_is_refused() => RunAsync(async database =>
    {
        var original = await OpenAsync(database, Terminal1);
        var sale = await SellAsync(database, "refund-source");
        await CloseAsync(database, original.Id, 200m);

        await using var db = Ctx(database);
        var store = new CashSessionStore(db);
        var recorder = new SaleCashRefundRecorder(db, store, new CashSessionMovementRecorder(db, store, Events(db), TimeProvider.System));
        var saleEntity = await db.Sales.Include(s => s.Payments).SingleAsync(s => s.Id == sale.Id);
        await using var tx = await store.BeginTransactionAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<CashSessionException>(() => recorder.RecordAsync(
            new SaleCashRefundRequest(saleEntity, SaleCashRefundKind.Void, Manager, null, true, null, "no session"), CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.CashRefundSessionRequired, error.Code);
        await tx.RollbackAsync(CancellationToken.None);
    });

    // ── variance approval -> asynchronous journal ────────────────────────────────────────────────────────────────────

    [Fact]
    public Task Variance_is_pending_after_close_and_reviewer_must_differ_from_the_closer() => RunAsync(async database =>
    {
        var session = await OpenAsync(database);
        await CloseAsync(database, session.Id, 80m, "short 20", user: Manager);

        await using var db = Ctx(database);
        var handler = new ReviewCashDifferenceCommandHandler(
            new CashDifferenceStore(db), new CashSessionStore(db), AllowAllReadAuthorizer(), Events(db),
            Mock.Of<ICashSessionAuditWriter>(), TimeProvider.System);
        var approvalId = await db.CashDifferenceApprovals.Select(a => a.Id).SingleAsync();

        var self = await Assert.ThrowsAsync<CashSessionException>(() =>
            handler.Handle(new ReviewCashDifferenceCommand(approvalId, true, null, Manager), CancellationToken.None));
        Assert.Equal(CashSessionErrorCodes.VarianceSelfReview, self.Code);
        Assert.Equal(CashDifferencePostingStatuses.NotRequired, await ScalarAsync<string>(database, "SELECT posting_status FROM cash_difference_approvals"));

        var approved = await handler.Handle(new ReviewCashDifferenceCommand(approvalId, true, "ok", Reviewer), CancellationToken.None);
        Assert.Equal(CashDifferenceStatuses.Approved, approved.Approval.Status);
        Assert.Equal(CashDifferencePostingStatuses.Pending, approved.Approval.PostingStatus);

        var replay = await handler.Handle(new ReviewCashDifferenceCommand(approvalId, true, "ok", Reviewer), CancellationToken.None);
        Assert.True(replay.Replayed);
        Assert.Equal(2, await ScalarAsync<int>(database,
            $"SELECT count(*)::int FROM integration_outbox_messages WHERE message_type IN ('{CashSessionEventTypes.VarianceRaised}', '{CashSessionEventTypes.VarianceDecided}')"));
    });

    private static ICashSessionOutletAuthorizer AllowAllReadAuthorizer()
    {
        var authorizer = new Mock<ICashSessionOutletAuthorizer>();
        authorizer.Setup(a => a.AuthorizeReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long outlet, CancellationToken _) => outlet);
        return authorizer.Object;
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    /// <summary>Returns the first instant once and the second instant for every later reading.</summary>
    private sealed class SteppingClock(DateTime first, DateTime later) : TimeProvider
    {
        private int _reads;

        public override DateTimeOffset GetUtcNow() =>
            new(DateTime.SpecifyKind(Interlocked.Increment(ref _reads) == 1 ? first : later, DateTimeKind.Utc));
    }
}
