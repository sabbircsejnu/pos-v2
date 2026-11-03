using Moq;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.Pos;

internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public override DateTimeOffset GetUtcNow() => new(UtcNow);
}

internal sealed class RecordingCashSessionEvents : ICashSessionEventWriter
{
    public List<CashSessionEvent> Events { get; } = [];

    public Task AppendAsync(CashSessionEvent cashSessionEvent, CancellationToken cancellationToken)
    {
        Events.Add(cashSessionEvent);
        return Task.CompletedTask;
    }
}

internal sealed class NoopCashSessionTransaction : ICashSessionTransaction
{
    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        RolledBack = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class OpenCashSessionCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 18, 30, 0, DateTimeKind.Utc); // 00:30 on 4 Nov in Dhaka

    private sealed class Harness
    {
        public Mock<ICashSessionStore> Store { get; } = new();
        public Mock<IBusinessPosSettingsStore> Settings { get; } = new();
        public RecordingCashSessionEvents Events { get; } = new();
        public NoopCashSessionTransaction Transaction { get; } = new();
        public CashSession? Added { get; private set; }
        public OpenCashSessionCommandHandler Handler { get; }

        public Harness(
            IReadOnlyList<CashSessionTerminalInfo>? terminals = null,
            string? outletZone = null,
            int hours = 20,
            string businessZone = "Asia/Dhaka")
        {
            var authorizer = new Mock<ICashSessionWriteOutletAuthorizer>();
            authorizer.Setup(a => a.AuthorizeWriteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long outlet, CancellationToken _) => outlet);
            Store.Setup(s => s.GetOpenContextAsync(5001, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CashSessionOpenContext(
                    1, outletZone, terminals ?? [new CashSessionTerminalInfo(8001, "Counter 1", "C1")], "Main"));
            Store.Setup(s => s.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Transaction);
            Store.Setup(s => s.AddOpenSessionAsync(It.IsAny<CashSession>(), It.IsAny<CancellationToken>()))
                .Callback<CashSession, CancellationToken>((session, _) =>
                {
                    session.Id = 9001;
                    Added = session;
                })
                .ReturnsAsync(CashSessionAddResult.Created);
            Settings.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessPosSettingsSnapshot(
                    1, hours, businessZone, true, 10m, "VAT", false, 0m, 1, null, null, true));

            Handler = new OpenCashSessionCommandHandler(
                Store.Object,
                authorizer.Object,
                Settings.Object,
                Mock.Of<ICashSessionOpenAuditWriter>(),
                Mock.Of<ICashSessionOpenedPublisher>(),
                Events,
                new FixedTimeProvider(Now));
        }
    }

    [Fact]
    public async Task Server_derives_business_date_zone_and_duration_snapshot()
    {
        var harness = new Harness(hours: 12);

        var result = await harness.Handler.Handle(
            new OpenCashSessionCommand(5001, 8001, 125m, " Morning ", 7001, null), CancellationToken.None);

        Assert.Equal(new DateTime(2026, 11, 4), harness.Added!.BusinessDate);
        Assert.Equal("Asia/Dhaka", harness.Added.TimeZoneIdSnapshot);
        Assert.Equal(12, harness.Added.DurationSnapshotHours);
        Assert.Equal(Now, harness.Added.OpenedAt);
        Assert.Equal("Morning", harness.Added.OpeningNotes);
        Assert.Equal(7001, harness.Added.OpenedByUserId);
        Assert.Equal("open", result.State);
        Assert.Equal(Now.AddHours(12), result.CutoffAtUtc);
        Assert.True(harness.Transaction.Committed);
    }

    [Fact]
    public async Task Outlet_time_zone_override_wins_over_the_business_default()
    {
        var harness = new Harness(outletZone: "America/New_York");

        await harness.Handler.Handle(new OpenCashSessionCommand(5001, 8001, 0m, null, 7001), CancellationToken.None);

        Assert.Equal("America/New_York", harness.Added!.TimeZoneIdSnapshot);
        Assert.Equal(new DateTime(2026, 11, 3), harness.Added.BusinessDate);
    }

    [Fact]
    public async Task Opening_event_is_written_to_the_outbox_in_the_same_transaction()
    {
        var harness = new Harness();

        await harness.Handler.Handle(new OpenCashSessionCommand(5001, 8001, 10m, null, 7001), CancellationToken.None);

        var opened = Assert.Single(harness.Events.Events);
        Assert.Equal(CashSessionEventTypes.Opened, opened.MessageType);
        Assert.Equal("9001", opened.AggregateId);
        harness.Store.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_single_active_terminal_is_selected_automatically()
    {
        var harness = new Harness();

        await harness.Handler.Handle(new OpenCashSessionCommand(5001, 0, 0m, null, 7001), CancellationToken.None);

        Assert.Equal(8001, harness.Added!.TerminalId);
    }

    [Fact]
    public async Task Several_terminals_require_an_explicit_choice()
    {
        var harness = new Harness(terminals: [new(8001, "A", "A"), new(8002, "B", "B")]);

        var error = await Assert.ThrowsAsync<CashSessionException>(() =>
            harness.Handler.Handle(new OpenCashSessionCommand(5001, 0, 0m, null, 7001), CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.TerminalRequired, error.Code);
        Assert.Null(harness.Added);
    }

    [Fact]
    public async Task No_active_terminal_blocks_opening()
    {
        var harness = new Harness(terminals: []);

        var error = await Assert.ThrowsAsync<CashSessionException>(() =>
            harness.Handler.Handle(new OpenCashSessionCommand(5001, 0, 0m, null, 7001), CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.NoActiveTerminal, error.Code);
    }

    [Fact]
    public async Task A_terminal_of_another_outlet_is_rejected()
    {
        var harness = new Harness();

        var error = await Assert.ThrowsAsync<CashSessionException>(() =>
            harness.Handler.Handle(new OpenCashSessionCommand(5001, 9999, 0m, null, 7001), CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.TerminalInvalid, error.Code);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("10.005")]
    public async Task Opening_cash_must_be_non_negative_with_two_decimals(string amount)
    {
        var harness = new Harness();

        var error = await Assert.ThrowsAsync<CashSessionException>(() =>
            harness.Handler.Handle(
                new OpenCashSessionCommand(5001, 8001, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), null, 7001),
                CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.OpeningCashInvalid, error.Code);
    }

    [Fact]
    public async Task A_second_open_session_on_the_terminal_is_a_conflict_and_rolls_back()
    {
        var harness = new Harness();
        harness.Store.Setup(s => s.AddOpenSessionAsync(It.IsAny<CashSession>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CashSessionAddResult.TerminalAlreadyOpen);

        var error = await Assert.ThrowsAsync<CashSessionException>(() =>
            harness.Handler.Handle(new OpenCashSessionCommand(5001, 8001, 0m, null, 7001), CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.AlreadyOpen, error.Code);
        Assert.Equal(409, error.HttpStatus);
        Assert.True(harness.Transaction.RolledBack);
        Assert.Empty(harness.Events.Events);
    }

    [Fact]
    public async Task Idempotent_retry_returns_the_existing_session_without_a_new_one()
    {
        var harness = new Harness();
        var existing = new CashSession
        {
            Id = 77, BusinessId = 1, OutletId = 5001, TerminalId = 8001, OpenedAt = Now.AddMinutes(-1),
            BusinessDate = new DateTime(2026, 11, 4), OpeningCashAmount = 50m, OpenedByUserId = 7001,
            DurationSnapshotHours = 20, TimeZoneIdSnapshot = "Asia/Dhaka", OpenIdempotencyKey = "k1"
        };
        harness.Store.Setup(s => s.FindByIdempotencyKeyAsync(5001, "k1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await harness.Handler.Handle(
            new OpenCashSessionCommand(5001, 8001, 50m, null, 7001, "k1"), CancellationToken.None);

        Assert.Equal(77, result.Id);
        Assert.Null(harness.Added);
    }

    [Fact]
    public async Task Idempotency_key_reused_for_a_different_request_is_a_conflict()
    {
        var harness = new Harness();
        var existing = new CashSession
        {
            Id = 77, BusinessId = 1, OutletId = 5001, TerminalId = 8001, OpenedAt = Now, OpeningCashAmount = 50m,
            OpenedByUserId = 7001, OpenIdempotencyKey = "k1"
        };
        harness.Store.Setup(s => s.FindByIdempotencyKeyAsync(5001, "k1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var error = await Assert.ThrowsAsync<CashSessionException>(() => harness.Handler.Handle(
            new OpenCashSessionCommand(5001, 8001, 999m, null, 7001, "k1"), CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.IdempotencyConflict, error.Code);
    }
}
