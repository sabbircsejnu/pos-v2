using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record ApproveExpenseCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId,
    long? ExpenseAccountId)
    : ICommand<long>;

public sealed class ApproveExpenseCommandHandler(
    IExpenseCommandStore store)
    : IRequestHandler<ApproveExpenseCommand, long>
{
    public Task<long> Handle(
        ApproveExpenseCommand request,
        CancellationToken cancellationToken) =>
        store.ApproveAsync(
            request.BusinessId,
            request.ActorUserId,
            request.ExpenseId,
            request.ExpenseAccountId,
            cancellationToken);
}
