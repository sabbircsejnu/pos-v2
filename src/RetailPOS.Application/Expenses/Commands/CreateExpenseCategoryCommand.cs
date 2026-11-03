using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record CreateExpenseCategoryCommand(
    long BusinessId,
    long ActorUserId,
    ExpenseCategoryCommandInput Category)
    : ICommand<ExpenseCategoryCommandResult>;

public sealed class CreateExpenseCategoryCommandHandler(
    IExpenseCommandStore store)
    : IRequestHandler<CreateExpenseCategoryCommand, ExpenseCategoryCommandResult>
{
    public Task<ExpenseCategoryCommandResult> Handle(
        CreateExpenseCategoryCommand request,
        CancellationToken cancellationToken) =>
        store.CreateCategoryAsync(
            request.BusinessId,
            request.ActorUserId,
            request.Category,
            cancellationToken);
}
