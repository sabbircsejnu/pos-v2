using System.Text.Json;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Commands;

public sealed record GenerateProductCombinationsCommand(long ProductId)
    : ICommand<IReadOnlyList<ProductCombinationModel>>;

public sealed class GenerateProductCombinationsCommandHandler(
    IProductCombinationGenerationStore store,
    IProductIdentifierResolver identifierResolver)
    : IRequestHandler<GenerateProductCombinationsCommand, IReadOnlyList<ProductCombinationModel>>
{
    public async Task<IReadOnlyList<ProductCombinationModel>> Handle(
        GenerateProductCombinationsCommand request,
        CancellationToken cancellationToken)
    {
        var data = await store.GetGenerationDataAsync(
                request.ProductId,
                variationIds: null,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Product with ID {request.ProductId} not found");

        if (data.AssignedVariationIds.Count == 0)
        {
            throw new InvalidOperationException("No variations assigned to this product");
        }

        if (data.OptionGroups.Any(group => group.Count == 0))
        {
            throw new InvalidOperationException(
                "One or more selected variations have no options selected. " +
                "Please select at least one option per variation before generating combinations.");
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

internal static class ProductCombinationDraftGenerator
{
    public static IReadOnlyList<ProductCombinationDraft> Generate(
        IReadOnlyList<IReadOnlyList<ProductCombinationOption>> optionGroups,
        IProductIdentifierResolver identifierResolver)
    {
        var combinations = new List<ProductCombinationDraft>();
        AddCombinations(optionGroups, 0, [], combinations, identifierResolver);
        return combinations;
    }

    private static void AddCombinations(
        IReadOnlyList<IReadOnlyList<ProductCombinationOption>> optionGroups,
        int groupIndex,
        List<ProductCombinationOption> current,
        List<ProductCombinationDraft> result,
        IProductIdentifierResolver identifierResolver)
    {
        if (groupIndex == optionGroups.Count)
        {
            var name = string.Join(
                " / ",
                current.Select(option =>
                    string.IsNullOrWhiteSpace(option.VariationName)
                        ? option.OptionName
                        : $"{option.VariationName}: {option.OptionName}"));
            var (sizeCode, attributeCode) = identifierResolver.ExtractCodesFromOptions(
                current.Select(option => (option.VariationName, option.OptionName)));
            var attributes = current
                .Where(option => !string.IsNullOrWhiteSpace(option.VariationName))
                .ToDictionary(option => option.VariationName, option => option.OptionName);

            result.Add(new ProductCombinationDraft(
                name,
                sizeCode,
                attributeCode,
                current.Sum(option => option.PriceAdjustment),
                current.Select(option => option.OptionId).OrderBy(id => id).ToArray(),
                current.ToArray(),
                JsonSerializer.Serialize(attributes)));
            return;
        }

        foreach (var option in optionGroups[groupIndex])
        {
            current.Add(option);
            AddCombinations(optionGroups, groupIndex + 1, current, result, identifierResolver);
            current.RemoveAt(current.Count - 1);
        }
    }
}
