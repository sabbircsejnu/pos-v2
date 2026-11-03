using MediatR;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Queries;

public sealed record GetExpenseCategoriesQuery(long BusinessId, bool IncludeInactive)
    : IRequest<IReadOnlyList<ExpenseCategoryReadModel>>;

public sealed class GetExpenseCategoriesQueryHandler(IExpenseReadStore readStore)
    : IRequestHandler<GetExpenseCategoriesQuery, IReadOnlyList<ExpenseCategoryReadModel>>
{
    public Task<IReadOnlyList<ExpenseCategoryReadModel>> Handle(
        GetExpenseCategoriesQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetCategoriesAsync(
            request.BusinessId,
            request.IncludeInactive,
            cancellationToken);
}

public sealed record SearchExpensesQuery(ExpenseSearchFilter Filter)
    : IRequest<ExpenseListReadModel>;

public sealed class SearchExpensesQueryHandler(IExpenseReadStore readStore)
    : IRequestHandler<SearchExpensesQuery, ExpenseListReadModel>
{
    public Task<ExpenseListReadModel> Handle(
        SearchExpensesQuery request,
        CancellationToken cancellationToken) =>
        readStore.SearchAsync(request.Filter, cancellationToken);
}

public sealed record GetExpenseByIdQuery(long BusinessId, long ExpenseId)
    : IRequest<ExpenseReadModel>;

public sealed class GetExpenseByIdQueryHandler(IExpenseReadStore readStore)
    : IRequestHandler<GetExpenseByIdQuery, ExpenseReadModel>
{
    public Task<ExpenseReadModel> Handle(
        GetExpenseByIdQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetByIdAsync(request.BusinessId, request.ExpenseId, cancellationToken);
}

public sealed record GetCurrentMonthExpenseSummaryQuery(long BusinessId)
    : IRequest<ExpenseSummaryReadModel>;

public sealed class GetCurrentMonthExpenseSummaryQueryHandler(IExpenseReadStore readStore)
    : IRequestHandler<GetCurrentMonthExpenseSummaryQuery, ExpenseSummaryReadModel>
{
    public Task<ExpenseSummaryReadModel> Handle(
        GetCurrentMonthExpenseSummaryQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetCurrentMonthSummaryAsync(request.BusinessId, cancellationToken);
}
