using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record RejectExpenseCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId,
    string Reason)
    : ICommand<long>;

public sealed class RejectExpenseCommandHandler(
    IExpenseCommandStore store)
    : IRequestHandler<RejectExpenseCommand, long>
{
    public Task<long> Handle(
        RejectExpenseCommand request,
        CancellationToken cancellationToken) =>
        store.RejectAsync(
            request.BusinessId,
            request.ActorUserId,
            request.ExpenseId,
            request.Reason,
            cancellationToken);
}
