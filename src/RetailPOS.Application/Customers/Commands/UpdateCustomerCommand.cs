using MediatR;
using RetailPOS.Application.Customers.Abstractions;

namespace RetailPOS.Application.Customers.Commands;

public sealed record UpdateCustomerCommand(
    long Id,
    string Name,
    string? Phone,
    string? Email) : IRequest<CustomerReadModel>;

public sealed class UpdateCustomerCommandHandler(ICustomerUpdateStore updateStore)
    : IRequestHandler<UpdateCustomerCommand, CustomerReadModel>
{
    public async Task<CustomerReadModel> Handle(
        UpdateCustomerCommand request,
        CancellationToken cancellationToken)
    {
        var snapshot = await updateStore.GetForUpdateAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer with ID {request.Id} not found");

        if (snapshot.IsSystem)
            throw new InvalidOperationException("System customer cannot be edited");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Customer name is required");

        string? normalizedPhone = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            normalizedPhone = CustomerPhoneNormalizer.Normalize(request.Phone);
            var unchangedLegacyPhone = string.Equals(
                request.Phone.Trim(), snapshot.Customer.Phone?.Trim(), StringComparison.Ordinal);
            // A stored phone that predates normalization stays editable (name/email) while it is left unchanged.
            if (normalizedPhone is null && !unchangedLegacyPhone)
                throw new InvalidOperationException(
                    "Phone number is not valid. Use a Bangladesh mobile number or an international number starting with +.");
            if (normalizedPhone is not null)
            {
                var existing = await updateStore.FindByNormalizedPhoneAsync(normalizedPhone, request.Id, cancellationToken);
                if (existing is not null)
                    throw new CustomerPhoneConflictException(existing);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Email) &&
            await updateStore.EmailExistsAsync(request.Email, request.Id, cancellationToken))
            throw new InvalidOperationException(
                $"A customer with email '{request.Email}' already exists");

        return await updateStore.UpdateAsync(
            new CustomerUpdateValues(
                request.Id,
                request.Name.Trim(),
                request.Phone?.Trim(),
                request.Email?.Trim()?.ToLower(),
                normalizedPhone),
            cancellationToken);
    }
}
