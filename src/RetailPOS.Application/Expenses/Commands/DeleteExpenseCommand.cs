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
    IExpenseCommandStore store,
    IExpenseAttachmentStore attachments)
    : IRequestHandler<DeleteExpenseCommand, long>
{
    public async Task<long> Handle(
        DeleteExpenseCommand request,
        CancellationToken cancellationToken)
    {
        var storageKeys = await attachments.GetStorageKeysAsync(
            request.BusinessId,
            request.ExpenseId,
            cancellationToken);
        var deletedId = await store.DeleteAsync(
            request.BusinessId,
            request.ActorUserId,
            request.ExpenseId,
            cancellationToken);

        // Rows go with the expense; files only after the delete has been accepted and committed.
        attachments.RemoveFiles(storageKeys);
        return deletedId;
    }
}
