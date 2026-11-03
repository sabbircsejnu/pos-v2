using MediatR;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts.Commands;

public sealed record AcceptMobileStockTakeSubmissionCommand(
    AcceptMobileStockTakeSubmissionInput Input)
    : IRequest<AcceptMobileStockTakeSubmissionResult>;

public sealed class AcceptMobileStockTakeSubmissionCommandHandler(
    IMobileStockTakeSubmissionStore store)
    : IRequestHandler<AcceptMobileStockTakeSubmissionCommand, AcceptMobileStockTakeSubmissionResult>
{
    public Task<AcceptMobileStockTakeSubmissionResult> Handle(
        AcceptMobileStockTakeSubmissionCommand request,
        CancellationToken cancellationToken) =>
        store.AcceptAsync(request.Input, cancellationToken);
}
