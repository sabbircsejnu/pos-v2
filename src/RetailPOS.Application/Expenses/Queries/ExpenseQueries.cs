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

/// <summary>Without a source this is every enabled funding account; with one it is what that source may credit at the location.</summary>
public sealed record GetExpensePaymentAccountsQuery(
    long BusinessId,
    string? PaymentSource = null,
    long? OutletId = null,
    long? WarehouseId = null)
    : IRequest<IReadOnlyList<ExpensePaymentAccountReadModel>>;

public sealed class GetExpensePaymentAccountsQueryHandler(
    IExpenseReadStore readStore,
    IExpenseLocationAuthorizer locationAuthorizer)
    : IRequestHandler<GetExpensePaymentAccountsQuery, IReadOnlyList<ExpensePaymentAccountReadModel>>
{
    public async Task<IReadOnlyList<ExpensePaymentAccountReadModel>> Handle(
        GetExpensePaymentAccountsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OutletId.HasValue || request.WarehouseId.HasValue)
            await locationAuthorizer.AuthorizeAsync(request.OutletId, request.WarehouseId, cancellationToken);

        return await readStore.GetPaymentAccountsAsync(
            request.BusinessId,
            request.PaymentSource,
            request.OutletId,
            request.WarehouseId,
            cancellationToken);
    }
}

public sealed record GetEligibleExpenseAccountsQuery(long BusinessId)
    : IRequest<IReadOnlyList<ExpenseAccountOptionReadModel>>;

public sealed class GetEligibleExpenseAccountsQueryHandler(IExpenseReadStore readStore)
    : IRequestHandler<GetEligibleExpenseAccountsQuery, IReadOnlyList<ExpenseAccountOptionReadModel>>
{
    public Task<IReadOnlyList<ExpenseAccountOptionReadModel>> Handle(
        GetEligibleExpenseAccountsQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetEligibleExpenseAccountsAsync(request.BusinessId, cancellationToken);
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

public sealed record GetExpenseAttachmentsQuery(long BusinessId, long ExpenseId)
    : IRequest<IReadOnlyList<ExpenseAttachmentReadModel>>;

public sealed class GetExpenseAttachmentsQueryHandler(IExpenseAttachmentStore store)
    : IRequestHandler<GetExpenseAttachmentsQuery, IReadOnlyList<ExpenseAttachmentReadModel>>
{
    public Task<IReadOnlyList<ExpenseAttachmentReadModel>> Handle(
        GetExpenseAttachmentsQuery request,
        CancellationToken cancellationToken) =>
        store.ListAsync(request.BusinessId, request.ExpenseId, cancellationToken);
}

public sealed record GetExpenseAttachmentContentQuery(long BusinessId, long ExpenseId, long AttachmentId)
    : IRequest<ExpenseAttachmentContentReadModel>;

public sealed class GetExpenseAttachmentContentQueryHandler(IExpenseAttachmentStore store)
    : IRequestHandler<GetExpenseAttachmentContentQuery, ExpenseAttachmentContentReadModel>
{
    public Task<ExpenseAttachmentContentReadModel> Handle(
        GetExpenseAttachmentContentQuery request,
        CancellationToken cancellationToken) =>
        store.GetContentAsync(request.BusinessId, request.ExpenseId, request.AttachmentId, cancellationToken);
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
