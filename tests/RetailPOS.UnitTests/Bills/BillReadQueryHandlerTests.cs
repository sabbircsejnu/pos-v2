using RetailPOS.Application.Bills;
using RetailPOS.Application.Bills.Abstractions;
using RetailPOS.Application.Bills.Queries;

namespace RetailPOS.Tests.Bills;

public sealed class BillReadQueryHandlerTests
{
    [Fact]
    public async Task SearchAndSummaryQueries_UseTrustedBusinessScopeAndForwardCancellation()
    {
        var store = new FakeBillReadStore
        {
            SearchResult = new BillSearchReadModel([], 4),
            Summary = new BillSummaryReadModel(500, 125, 3, 1)
        };
        using var cancellation = new CancellationTokenSource();
        var scope = new FakeBillReadScopeProvider { BusinessId = 17 };

        var search = await new SearchBillsQueryHandler(store, scope).Handle(
            new SearchBillsQuery("UNPAID", 10, 2, 5),
            cancellation.Token);
        var summary = await new GetBillSummaryQueryHandler(store, scope).Handle(
            new GetBillSummaryQuery(),
            cancellation.Token);

        Assert.Same(store.SearchResult, search);
        Assert.Equal(store.Summary, summary);
        Assert.Equal("UNPAID", store.Status);
        Assert.Equal(10, store.SupplierId);
        Assert.Equal(2, store.PageNumber);
        Assert.Equal(5, store.PageSize);
        Assert.Equal(17, store.BusinessId);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task DetailQuery_UsesBusinessScopeAndPreservesNotFoundMessage()
    {
        var store = new FakeBillReadStore();
        var handler = new GetBillByIdQueryHandler(
            store,
            new FakeBillReadScopeProvider { BusinessId = 18 });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetBillByIdQuery(99), CancellationToken.None));

        Assert.Equal("Bill with ID 99 not found", exception.Message);
        Assert.Equal(99, store.BillId);
        Assert.Equal(18, store.BusinessId);
    }

    private sealed class FakeBillReadScopeProvider : IBillReadScopeProvider
    {
        public long BusinessId { get; init; }

        public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BusinessId);
        }
    }

    private sealed class FakeBillReadStore : IBillReadStore
    {
        public BillSearchReadModel SearchResult { get; init; } = new([], 0);
        public BillSummaryReadModel Summary { get; init; } = new(0, 0, 0, 0);
        public string? Status { get; private set; }
        public long? SupplierId { get; private set; }
        public int PageNumber { get; private set; }
        public int PageSize { get; private set; }
        public long BillId { get; private set; }
        public long BusinessId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<BillSearchReadModel> SearchAsync(
            string? status,
            long? supplierId,
            int pageNumber,
            int pageSize,
            long businessId,
            CancellationToken cancellationToken)
        {
            Status = status;
            SupplierId = supplierId;
            PageNumber = pageNumber;
            PageSize = pageSize;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(SearchResult);
        }

        public Task<BillReadModel?> GetByIdAsync(
            long billId,
            long businessId,
            CancellationToken cancellationToken)
        {
            BillId = billId;
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult<BillReadModel?>(null);
        }

        public Task<BillSummaryReadModel> GetSummaryAsync(
            long businessId,
            CancellationToken cancellationToken)
        {
            BusinessId = businessId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Summary);
        }
    }
}
