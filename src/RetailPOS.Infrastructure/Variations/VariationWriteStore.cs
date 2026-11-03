using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Variations;
using RetailPOS.Application.Variations.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Variations;

public sealed class VariationWriteStore(RetailPOSDbContext dbContext)
    : IVariationWriteStore
{
    public Task<bool> NameExistsAsync(
        string name,
        long? excludeVariationId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Variations.Where(variation => variation.Name == name);
        if (excludeVariationId.HasValue)
            query = query.Where(variation => variation.Id != excludeVariationId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public async Task<VariationReadModel> CreateAsync(
        VariationWriteModel input,
        CancellationToken cancellationToken)
    {
        var variation = new Variation
        {
            Name = input.Name,
            DisplayOrder = input.DisplayOrder,
            IsActive = input.IsActive,
            AutoSelectAllOptions = input.AutoSelectAllOptions
        };
        dbContext.Variations.Add(variation);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var inputOption in input.Options)
        {
            var option = new VariationOption
            {
                VariationId = variation.Id,
                Name = inputOption.Name,
                PriceAdjustment = inputOption.PriceAdjustment,
                DisplayOrder = inputOption.DisplayOrder,
                IsActive = inputOption.IsActive,
                Variation = variation
            };
            dbContext.VariationOptions.Add(option);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return VariationReadStore.ToReadModel(variation);
    }

    public async Task<VariationReadModel?> UpdateAsync(
        long variationId,
        VariationWriteModel input,
        CancellationToken cancellationToken)
    {
        var variation = await dbContext.Variations
            .Include(candidate => candidate.Options)
            .FirstOrDefaultAsync(candidate => candidate.Id == variationId, cancellationToken);
        if (variation is null)
            return null;

        variation.Name = input.Name;
        variation.DisplayOrder = input.DisplayOrder;
        variation.IsActive = input.IsActive;
        variation.AutoSelectAllOptions = input.AutoSelectAllOptions;
        variation.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return VariationReadStore.ToReadModel(variation);
    }

    public async Task DeleteAsync(long variationId, CancellationToken cancellationToken)
    {
        var variation = await dbContext.Variations.FindAsync([variationId], cancellationToken);
        if (variation is null)
            return;

        dbContext.Variations.Remove(variation);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<VariationOptionReadModel> CreateOptionAsync(
        long variationId,
        VariationOptionWriteModel input,
        CancellationToken cancellationToken)
    {
        var option = new VariationOption
        {
            VariationId = variationId,
            Name = input.Name,
            PriceAdjustment = input.PriceAdjustment,
            DisplayOrder = input.DisplayOrder,
            IsActive = input.IsActive
        };
        dbContext.VariationOptions.Add(option);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await dbContext.VariationOptions
            .AsNoTracking()
            .Where(candidate => candidate.Id == option.Id)
            .Select(candidate => new VariationOptionReadModel(
                candidate.Id,
                candidate.VariationId,
                candidate.Variation.Name,
                candidate.Name,
                candidate.PriceAdjustment,
                candidate.DisplayOrder,
                candidate.IsActive))
            .SingleAsync(cancellationToken);
    }

    public async Task<VariationOptionReadModel?> UpdateOptionAsync(
        long optionId,
        VariationOptionWriteModel input,
        CancellationToken cancellationToken)
    {
        var option = await dbContext.VariationOptions
            .Include(candidate => candidate.Variation)
            .FirstOrDefaultAsync(candidate => candidate.Id == optionId, cancellationToken);
        if (option is null)
            return null;

        option.Name = input.Name;
        option.PriceAdjustment = input.PriceAdjustment;
        option.DisplayOrder = input.DisplayOrder;
        option.IsActive = input.IsActive;
        option.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new VariationOptionReadModel(
            option.Id,
            option.VariationId,
            option.Variation.Name,
            option.Name,
            option.PriceAdjustment,
            option.DisplayOrder,
            option.IsActive);
    }

    public async Task DeleteOptionAsync(long optionId, CancellationToken cancellationToken)
    {
        var option = await dbContext.VariationOptions.FindAsync([optionId], cancellationToken);
        if (option is null)
            return;

        dbContext.VariationOptions.Remove(option);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
