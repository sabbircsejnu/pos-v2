using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record UploadExpenseAttachmentCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId,
    Stream Content,
    string? FileName)
    : ICommand<ExpenseAttachmentReadModel>;

public sealed class UploadExpenseAttachmentCommandHandler(IExpenseAttachmentStore store)
    : IRequestHandler<UploadExpenseAttachmentCommand, ExpenseAttachmentReadModel>
{
    public Task<ExpenseAttachmentReadModel> Handle(
        UploadExpenseAttachmentCommand request,
        CancellationToken cancellationToken) =>
        store.UploadAsync(
            request.BusinessId,
            request.ActorUserId,
            request.ExpenseId,
            request.Content,
            request.FileName,
            cancellationToken);
}

public sealed record DeleteExpenseAttachmentCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId,
    long AttachmentId)
    : ICommand<Unit>;

public sealed class DeleteExpenseAttachmentCommandHandler(IExpenseAttachmentStore store)
    : IRequestHandler<DeleteExpenseAttachmentCommand, Unit>
{
    public async Task<Unit> Handle(
        DeleteExpenseAttachmentCommand request,
        CancellationToken cancellationToken)
    {
        await store.DeleteAsync(
            request.BusinessId,
            request.ActorUserId,
            request.ExpenseId,
            request.AttachmentId,
            cancellationToken);
        return Unit.Value;
    }
}
