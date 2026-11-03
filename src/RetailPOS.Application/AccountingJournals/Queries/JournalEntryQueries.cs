using MediatR;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Queries;

public sealed record SearchJournalEntriesQuery(JournalSearchFilter Filter)
    : IRequest<JournalListReadModel>;

public sealed class SearchJournalEntriesQueryHandler(IJournalEntryReadStore readStore)
    : IRequestHandler<SearchJournalEntriesQuery, JournalListReadModel>
{
    public Task<JournalListReadModel> Handle(
        SearchJournalEntriesQuery request,
        CancellationToken cancellationToken) =>
        readStore.SearchAsync(request.Filter, cancellationToken);
}

public sealed record GetJournalEntryByIdQuery(long BusinessId, long JournalEntryId)
    : IRequest<JournalEntryReadModel>;

public sealed class GetJournalEntryByIdQueryHandler(IJournalEntryReadStore readStore)
    : IRequestHandler<GetJournalEntryByIdQuery, JournalEntryReadModel>
{
    public Task<JournalEntryReadModel> Handle(
        GetJournalEntryByIdQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetByIdAsync(
            request.BusinessId,
            request.JournalEntryId,
            cancellationToken);
}
