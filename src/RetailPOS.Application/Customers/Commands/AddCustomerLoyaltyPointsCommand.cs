using MediatR;
using RetailPOS.Application.Customers.Abstractions;

namespace RetailPOS.Application.Customers.Commands;

public sealed record AddCustomerLoyaltyPointsCommand(long Id, int Points)
    : IRequest<CustomerReadModel>;

public sealed class AddCustomerLoyaltyPointsCommandHandler(ICustomerLoyaltyStore loyaltyStore)
    : IRequestHandler<AddCustomerLoyaltyPointsCommand, CustomerReadModel>
{
    public async Task<CustomerReadModel> Handle(
        AddCustomerLoyaltyPointsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Points <= 0)
            throw new InvalidOperationException("Points must be greater than zero");

        var customer = await loyaltyStore.GetForLoyaltyUpdateAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer with ID {request.Id} not found");

        return await loyaltyStore.SetLoyaltyPointsAsync(
            customer.Id,
            customer.LoyaltyPoints + request.Points,
            cancellationToken);
    }
}
