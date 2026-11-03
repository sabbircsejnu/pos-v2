using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record SubmitExpenseCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId)
    : ICommand<long>;

public sealed class SubmitExpenseCommandHandler(
    IExpenseCommandStore store)
    : IRequestHandler<SubmitExpenseCommand, long>
{
    public Task<long> Handle(
        SubmitExpenseCommand request,
        CancellationToken cancellationToken) =>
        store.SubmitAsync(
            request.BusinessId,
            request.ActorUserId,
            request.ExpenseId,
            cancellationToken);
}
