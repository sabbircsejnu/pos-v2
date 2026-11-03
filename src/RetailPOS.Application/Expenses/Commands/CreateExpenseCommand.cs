using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record CreateExpenseCommand(
    long BusinessId,
    long ActorUserId,
    ExpenseCreationInput Expense)
    : ICommand<long>;

public sealed class CreateExpenseCommandHandler(
    IExpenseCommandStore store)
    : IRequestHandler<CreateExpenseCommand, long>
{
    public Task<long> Handle(
        CreateExpenseCommand request,
        CancellationToken cancellationToken) =>
        store.CreateAsync(
            request.BusinessId,
            request.ActorUserId,
            request.Expense,
            cancellationToken);
}
