using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record DeleteExpenseCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId)
    : ICommand<long>;

public sealed class DeleteExpenseCommandHandler(
    IExpenseCommandStore store)
    : IRequestHandler<DeleteExpenseCommand, long>
{
    public Task<long> Handle(
        DeleteExpenseCommand request,
        CancellationToken cancellationToken) =>
        store.DeleteAsync(
            request.BusinessId,
            request.ActorUserId,
            request.ExpenseId,
            cancellationToken);
}
