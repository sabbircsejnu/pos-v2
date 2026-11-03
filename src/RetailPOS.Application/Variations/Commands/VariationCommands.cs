using MediatR;
using RetailPOS.Application.Variations.Abstractions;

namespace RetailPOS.Application.Variations.Commands;

public sealed record CreateVariationCommand(VariationWriteModel Variation)
    : IRequest<VariationReadModel>;

public sealed class CreateVariationCommandHandler(IVariationWriteStore writeStore)
    : IRequestHandler<CreateVariationCommand, VariationReadModel>
{
    public async Task<VariationReadModel> Handle(
        CreateVariationCommand request,
        CancellationToken cancellationToken)
    {
        if (await writeStore.NameExistsAsync(request.Variation.Name, null, cancellationToken))
            throw new Exception($"Variation with name '{request.Variation.Name}' already exists");

        return await writeStore.CreateAsync(request.Variation, cancellationToken);
    }
}

public sealed record UpdateVariationCommand(long VariationId, VariationWriteModel Variation)
    : IRequest<VariationReadModel>;

public sealed class UpdateVariationCommandHandler(
    IVariationReadStore readStore,
    IVariationWriteStore writeStore)
    : IRequestHandler<UpdateVariationCommand, VariationReadModel>
{
    public async Task<VariationReadModel> Handle(
        UpdateVariationCommand request,
        CancellationToken cancellationToken)
    {
        if (await readStore.GetByIdAsync(request.VariationId, cancellationToken) is null)
            throw new Exception("Variation not found");

        if (await writeStore.NameExistsAsync(
                request.Variation.Name,
                request.VariationId,
                cancellationToken))
            throw new Exception($"Variation with name '{request.Variation.Name}' already exists");

        return await writeStore.UpdateAsync(
                request.VariationId,
                request.Variation,
                cancellationToken)
            ?? throw new Exception("Variation not found");
    }
}

public sealed record DeleteVariationCommand(long VariationId) : IRequest;

public sealed class DeleteVariationCommandHandler(
    IVariationReadStore readStore,
    IVariationWriteStore writeStore)
    : IRequestHandler<DeleteVariationCommand>
{
    public async Task Handle(DeleteVariationCommand request, CancellationToken cancellationToken)
    {
        var variation = await readStore.GetByIdAsync(request.VariationId, cancellationToken);
        if (variation is null)
            throw new KeyNotFoundException("Variation not found");

        var products = await readStore.GetProductsUsingVariationAsync(
            request.VariationId,
            cancellationToken);
        if (products.Count > 0)
            throw new VariationInUseException(
                $"Cannot delete variation '{variation.Name}' because it is used by {products.Count} product(s).",
                products);

        await writeStore.DeleteAsync(request.VariationId, cancellationToken);
    }
}

public sealed record CreateVariationOptionCommand(
    long VariationId,
    VariationOptionWriteModel Option) : IRequest<VariationOptionReadModel>;

public sealed class CreateVariationOptionCommandHandler(IVariationWriteStore writeStore)
    : IRequestHandler<CreateVariationOptionCommand, VariationOptionReadModel>
{
    public Task<VariationOptionReadModel> Handle(
        CreateVariationOptionCommand request,
        CancellationToken cancellationToken) =>
        writeStore.CreateOptionAsync(request.VariationId, request.Option, cancellationToken);
}

public sealed record UpdateVariationOptionCommand(
    long OptionId,
    VariationOptionWriteModel Option) : IRequest<VariationOptionReadModel>;

public sealed class UpdateVariationOptionCommandHandler(
    IVariationReadStore readStore,
    IVariationWriteStore writeStore)
    : IRequestHandler<UpdateVariationOptionCommand, VariationOptionReadModel>
{
    public async Task<VariationOptionReadModel> Handle(
        UpdateVariationOptionCommand request,
        CancellationToken cancellationToken)
    {
        if (await readStore.GetOptionByIdAsync(request.OptionId, cancellationToken) is null)
            throw new Exception("Option not found");

        return await writeStore.UpdateOptionAsync(
                request.OptionId,
                request.Option,
                cancellationToken)
            ?? throw new Exception("Option not found");
    }
}

public sealed record DeleteVariationOptionCommand(long OptionId) : IRequest;

public sealed class DeleteVariationOptionCommandHandler(IVariationWriteStore writeStore)
    : IRequestHandler<DeleteVariationOptionCommand>
{
    public Task Handle(DeleteVariationOptionCommand request, CancellationToken cancellationToken) =>
        writeStore.DeleteOptionAsync(request.OptionId, cancellationToken);
}
