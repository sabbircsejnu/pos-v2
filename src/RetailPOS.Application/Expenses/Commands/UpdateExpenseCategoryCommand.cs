using MediatR;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record UpdateExpenseCategoryCommand(
    long BusinessId,
    long ActorUserId,
    long CategoryId,
    ExpenseCategoryCommandInput Input) : IRequest<ExpenseCategoryCommandResult>;

public sealed class UpdateExpenseCategoryCommandHandler(IExpenseCommandStore store)
    : IRequestHandler<UpdateExpenseCategoryCommand, ExpenseCategoryCommandResult>
{
    public Task<ExpenseCategoryCommandResult> Handle(
        UpdateExpenseCategoryCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateCategoryAsync(
            request.BusinessId,
            request.ActorUserId,
            request.CategoryId,
            request.Input,
            cancellationToken);
}
