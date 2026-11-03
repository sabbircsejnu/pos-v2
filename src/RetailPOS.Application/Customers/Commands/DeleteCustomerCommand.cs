using MediatR;
using RetailPOS.Application.Customers.Abstractions;

namespace RetailPOS.Application.Customers.Commands;

public sealed record DeleteCustomerCommand(long Id) : IRequest<bool>;

public sealed class DeleteCustomerCommandHandler(ICustomerDeleteStore deleteStore)
    : IRequestHandler<DeleteCustomerCommand, bool>
{
    public async Task<bool> Handle(
        DeleteCustomerCommand request,
        CancellationToken cancellationToken)
    {
        var snapshot = await deleteStore.GetForDeleteAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer with ID {request.Id} not found");

        if (snapshot.IsSystem)
            throw new InvalidOperationException("System customer cannot be deleted");

        return await deleteStore.DeleteAsync(request.Id, cancellationToken);
    }
}
