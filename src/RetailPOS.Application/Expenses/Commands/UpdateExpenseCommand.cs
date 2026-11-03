using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record UpdateExpenseCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId,
    ExpenseUpdateInput Expense)
    : ICommand<long>;

public sealed class UpdateExpenseCommandHandler(
    IExpenseCommandStore store)
    : IRequestHandler<UpdateExpenseCommand, long>
{
    public Task<long> Handle(
        UpdateExpenseCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateAsync(
            request.BusinessId,
            request.ActorUserId,
            request.ExpenseId,
            request.Expense,
            cancellationToken);
}
