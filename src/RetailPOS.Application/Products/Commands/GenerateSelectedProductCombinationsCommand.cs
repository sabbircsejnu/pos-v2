using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Commands;

public sealed record GenerateSelectedProductCombinationsCommand(
    long ProductId,
    IReadOnlyList<long> VariationIds)
    : ICommand<IReadOnlyList<ProductCombinationModel>>;

public sealed class GenerateSelectedProductCombinationsCommandHandler(
    IProductCombinationGenerationStore store,
    IProductIdentifierResolver identifierResolver)
    : IRequestHandler<
        GenerateSelectedProductCombinationsCommand,
        IReadOnlyList<ProductCombinationModel>>
{
    public async Task<IReadOnlyList<ProductCombinationModel>> Handle(
        GenerateSelectedProductCombinationsCommand request,
        CancellationToken cancellationToken)
    {
        var data = await store.GetGenerationDataAsync(
                request.ProductId,
                request.VariationIds,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Product with ID {request.ProductId} not found");

        if (data.AssignedVariationIds.Count == 0)
        {
            throw new InvalidOperationException(
                $"Product {request.ProductId} has no variations assigned. " +
                "Please assign variations to this product first.");
        }

        if (request.VariationIds.Count > 0)
        {
            var assignedVariationIds = data.AssignedVariationIds.ToHashSet();
            var invalidIds = request.VariationIds
                .Where(id => !assignedVariationIds.Contains(id))
                .ToArray();
            if (invalidIds.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Variation ID(s) {string.Join(", ", invalidIds)} are not assigned to product {request.ProductId}. " +
                    $"Assigned variation IDs are: {string.Join(", ", assignedVariationIds.OrderBy(id => id))}.");
            }
        }

        if (data.OptionGroups.Count == 0)
        {
            throw new InvalidOperationException("No valid variations selected");
        }

        if (data.OptionGroups.Any(group => group.Count == 0))
        {
            throw new InvalidOperationException(
                "One or more selected variations have no options selected for this product.");
        }

        if (string.IsNullOrWhiteSpace(data.ProductCode))
        {
            throw new InvalidOperationException("Main Product Code is required before generating variants");
        }

        var combinations = ProductCombinationDraftGenerator.Generate(
            data.OptionGroups,
            identifierResolver);

        return await store.CreateMissingCombinationsAsync(
            data,
            combinations,
            cancellationToken);
    }
}
