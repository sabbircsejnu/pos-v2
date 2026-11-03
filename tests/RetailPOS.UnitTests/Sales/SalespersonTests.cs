using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.Queries;

namespace RetailPOS.Tests.Sales;

/// <summary>Phase 4 salesperson: idempotency fingerprint compatibility and the default-salesperson rule.</summary>
public sealed class SalespersonTests
{
    /// <summary>SHA-256 of the v1 canonical form "v1|5001|8001|~|7|~|0.00|0.00|cash|200.00|item|1|2|100.00|0.00|" (pre-Phase 4).</summary>
    private const string PrePhase4Fingerprint = "10d582a4e040cb29659c18a624ea504d97280b66e76e7ce6dca065eee6f56b33";

    [Fact]
    public void A_request_without_a_salesperson_keeps_its_pre_Phase_4_fingerprint()
    {
        Assert.Equal(PrePhase4Fingerprint, SaleRequestFingerprint.Compute(Command()));
    }

    [Fact]
    public void The_salesperson_is_part_of_the_fingerprint_when_supplied()
    {
        var without = SaleRequestFingerprint.Compute(Command());
        var withSeller = SaleRequestFingerprint.Compute(Command() with { SalespersonUserId = 12 });

        Assert.NotEqual(without, withSeller);
        Assert.NotEqual(withSeller, SaleRequestFingerprint.Compute(Command() with { SalespersonUserId = 13 }));
        Assert.Equal(withSeller, SaleRequestFingerprint.Compute(Command() with { SalespersonUserId = 12 }));
    }

    [Fact]
    public async Task The_session_opener_is_the_default_when_eligible()
    {
        var directory = new FakeDirectory(opener: 7, eligible: [7, 8, 9]);

        var result = await Handle(directory, terminalId: 8001, currentUserId: 8);

        Assert.Equal(7, result.DefaultSalespersonUserId);
        Assert.Equal(SalespersonDefaultSources.SessionOpener, result.DefaultSource);
        Assert.Equal([7L, 8L, 9L], result.Salespersons.Select(person => person.UserId));
    }

    [Fact]
    public async Task An_ineligible_opener_falls_back_to_the_current_user_and_then_to_none()
    {
        var openerLeft = new FakeDirectory(opener: 6, eligible: [7, 8]);

        var fallback = await Handle(openerLeft, terminalId: 8001, currentUserId: 8);
        Assert.Equal(8, fallback.DefaultSalespersonUserId);
        Assert.Equal(SalespersonDefaultSources.CurrentUser, fallback.DefaultSource);

        var none = await Handle(openerLeft, terminalId: 8001, currentUserId: 99);
        Assert.Null(none.DefaultSalespersonUserId);
        Assert.Equal(SalespersonDefaultSources.None, none.DefaultSource);
    }

    [Fact]
    public async Task Without_a_terminal_the_opener_is_not_consulted()
    {
        var directory = new FakeDirectory(opener: 7, eligible: [7, 8]);

        var result = await Handle(directory, terminalId: null, currentUserId: 8);

        Assert.Equal(8, result.DefaultSalespersonUserId);
        Assert.False(directory.OpenerQueried);
    }

    [Fact]
    public async Task An_unknown_outlet_is_reported_as_missing()
    {
        var directory = new FakeDirectory(opener: null, eligible: [], businessId: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Handle(directory, terminalId: null, currentUserId: 8));
    }

    private static Task<SalespersonOptionsReadModel> Handle(FakeDirectory directory, long? terminalId, long currentUserId) =>
        new GetEligibleSalespersonsQueryHandler(directory)
            .Handle(new GetEligibleSalespersonsQuery(5001, terminalId, currentUserId), CancellationToken.None);

    private static CreateSaleCommand Command() => new(
        5001,
        8001,
        null,
        [new CreateSaleLine(1, 2, 100m, 0m, null, null)],
        0m,
        0m,
        "cash",
        [],
        7,
        null,
        null,
        "key-1",
        200m);

    private sealed class FakeDirectory(long? opener, long[] eligible, long? businessId = 1) : ISaleSalespersonDirectory
    {
        public bool OpenerQueried { get; private set; }

        public Task<long?> GetOutletBusinessIdAsync(long outletId, CancellationToken cancellationToken) =>
            Task.FromResult(businessId);

        public Task<IReadOnlyList<EligibleSalespersonReadModel>> ListEligibleAsync(long businessId, long outletId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EligibleSalespersonReadModel>>(
                eligible.Select(id => new EligibleSalespersonReadModel(id, $"User {id}", true)).ToArray());

        public Task<long?> FindOpenSessionOpenerAsync(long outletId, long terminalId, CancellationToken cancellationToken)
        {
            OpenerQueried = true;
            return Task.FromResult(opener);
        }
    }
}
