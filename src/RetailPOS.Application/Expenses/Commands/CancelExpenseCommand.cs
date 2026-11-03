using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record CancelExpenseCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId,
    string Reason)
    : ICommand<long>;

public sealed class CancelExpenseCommandHandler(
    IExpenseCommandStore store)
    : IRequestHandler<CancelExpenseCommand, long>
{
    public Task<long> Handle(
        CancelExpenseCommand request,
        CancellationToken cancellationToken) =>
        store.CancelAsync(
            request.BusinessId,
            request.ActorUserId,
            request.ExpenseId,
            request.Reason,
            cancellationToken);
}
