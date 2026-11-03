using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Commands;

public sealed record CreatePriceRuleCommand(PriceRuleWriteData Rule)
    : ICommand<PriceRuleReadModel>;

public sealed class CreatePriceRuleCommandHandler(IPriceRuleCommandStore store)
    : IRequestHandler<CreatePriceRuleCommand, PriceRuleReadModel>
{
    public async Task<PriceRuleReadModel> Handle(
        CreatePriceRuleCommand request,
        CancellationToken cancellationToken)
    {
        await PriceRuleCommandValidation.ValidateAsync(request.Rule, store, cancellationToken);

        if (await store.NameExistsAsync(request.Rule.Name, null, cancellationToken))
        {
            throw new InvalidOperationException(
                $"A price rule named '{request.Rule.Name}' already exists");
        }

        var created = await store.CreateAsync(
            PriceRuleCommandValidation.Normalize(request.Rule),
            cancellationToken);
        return created;
    }
}

public sealed record UpdatePriceRuleCommand(long Id, PriceRuleWriteData Rule)
    : ICommand<PriceRuleReadModel>;

public sealed class UpdatePriceRuleCommandHandler(IPriceRuleCommandStore store)
    : IRequestHandler<UpdatePriceRuleCommand, PriceRuleReadModel>
{
    public async Task<PriceRuleReadModel> Handle(
        UpdatePriceRuleCommand request,
        CancellationToken cancellationToken)
    {
        _ = await store.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Price rule with ID {request.Id} not found");

        await PriceRuleCommandValidation.ValidateAsync(request.Rule, store, cancellationToken);

        if (await store.NameExistsAsync(request.Rule.Name, request.Id, cancellationToken))
        {
            throw new InvalidOperationException(
                $"A price rule named '{request.Rule.Name}' already exists");
        }

        var updated = await store.UpdateAsync(
            request.Id,
            PriceRuleCommandValidation.Normalize(request.Rule),
            cancellationToken)
            ?? throw new KeyNotFoundException($"Price rule with ID {request.Id} not found");

        return updated;
    }
}

public sealed record DeletePriceRuleCommand(long Id) : ICommand<bool>;

public sealed class DeletePriceRuleCommandHandler(IPriceRuleCommandStore store)
    : IRequestHandler<DeletePriceRuleCommand, bool>
{
    public async Task<bool> Handle(
        DeletePriceRuleCommand request,
        CancellationToken cancellationToken)
    {
        _ = await store.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Price rule with ID {request.Id} not found");

        _ = await store.DeleteAsync(request.Id, cancellationToken);
        return true;
    }
}
