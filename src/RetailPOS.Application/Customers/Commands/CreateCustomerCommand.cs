using MediatR;
using RetailPOS.Application.Customers.Abstractions;

namespace RetailPOS.Application.Customers.Commands;

public sealed record CreateCustomerCommand(
    string Name,
    string? Phone,
    string? Email) : IRequest<CustomerReadModel>;

public sealed class CreateCustomerCommandHandler(ICustomerCreateStore createStore)
    : IRequestHandler<CreateCustomerCommand, CustomerReadModel>
{
    public async Task<CustomerReadModel> Handle(
        CreateCustomerCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Customer name is required");

        string? normalizedPhone = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            normalizedPhone = CustomerPhoneNormalizer.Normalize(request.Phone)
                ?? throw new InvalidOperationException(
                    "Phone number is not valid. Use a Bangladesh mobile number or an international number starting with +.");
            var existing = await createStore.FindByNormalizedPhoneAsync(normalizedPhone, cancellationToken);
            if (existing is not null)
                throw new CustomerPhoneConflictException(existing);
        }

        if (!string.IsNullOrWhiteSpace(request.Email) &&
            await createStore.EmailExistsAsync(request.Email, cancellationToken))
            throw new InvalidOperationException(
                $"A customer with email '{request.Email}' already exists");

        return await createStore.CreateAsync(
            new CustomerCreateValues(
                request.Name.Trim(),
                request.Phone?.Trim(),
                request.Email?.Trim()?.ToLower(),
                normalizedPhone),
            cancellationToken);
    }
}
