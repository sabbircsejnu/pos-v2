using MediatR;
using RetailPOS.Application.OtherIncome.Abstractions;

namespace RetailPOS.Application.OtherIncome.Commands;

public sealed record ApproveOtherIncomeCommand(
    long BusinessId,
    long ActorUserId,
    long EntryId,
    long? IncomeAccountId) : IRequest<long>;

public sealed class ApproveOtherIncomeCommandHandler(IOtherIncomeCommandStore store)
    : IRequestHandler<ApproveOtherIncomeCommand, long>
{
    public Task<long> Handle(
        ApproveOtherIncomeCommand request,
        CancellationToken cancellationToken) =>
        store.ApproveAsync(
            request.BusinessId,
            request.ActorUserId,
            request.EntryId,
            request.IncomeAccountId,
            cancellationToken);
}
