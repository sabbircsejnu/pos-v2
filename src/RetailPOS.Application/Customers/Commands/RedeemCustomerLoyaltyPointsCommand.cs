using MediatR;
using RetailPOS.Application.Customers.Abstractions;

namespace RetailPOS.Application.Customers.Commands;

public sealed record RedeemCustomerLoyaltyPointsCommand(long Id, int Points)
    : IRequest<CustomerReadModel>;

public sealed class RedeemCustomerLoyaltyPointsCommandHandler(ICustomerLoyaltyStore loyaltyStore)
    : IRequestHandler<RedeemCustomerLoyaltyPointsCommand, CustomerReadModel>
{
    public async Task<CustomerReadModel> Handle(
        RedeemCustomerLoyaltyPointsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Points <= 0)
            throw new InvalidOperationException("Points must be greater than zero");

        var customer = await loyaltyStore.GetForLoyaltyUpdateAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer with ID {request.Id} not found");

        if (customer.LoyaltyPoints < request.Points)
            throw new InvalidOperationException(
                $"Insufficient loyalty points. Customer has {customer.LoyaltyPoints} points but {request.Points} were requested");

        return await loyaltyStore.SetLoyaltyPointsAsync(
            customer.Id,
            customer.LoyaltyPoints - request.Points,
            cancellationToken);
    }
}
