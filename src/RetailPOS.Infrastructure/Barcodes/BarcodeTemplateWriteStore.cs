using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Application.Barcodes.WriteModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Barcodes;

public sealed class BarcodeTemplateWriteStore(RetailPOSDbContext dbContext)
    : IBarcodeTemplateWriteStore
{
    public async Task<BarcodeTemplateReadModel> CreateAsync(
        BarcodeTemplateWriteModel writeModel,
        long businessId,
        long? userId,
        CancellationToken cancellationToken)
    {
        await EnsureNameAvailableAsync(businessId, writeModel.Name, null, cancellationToken);
        if (writeModel.IsDefault)
            await ClearDefaultAsync(businessId, cancellationToken);

        var template = new BarcodeTemplate
        {
            BusinessId = businessId,
            Name = writeModel.Name.Trim(),
            TemplateType = writeModel.TemplateType,
            PaperType = writeModel.PaperType,
            LabelWidthMm = writeModel.LabelWidthMm,
            LabelHeightMm = writeModel.LabelHeightMm,
            IsDefault = writeModel.IsDefault,
            IsActive = writeModel.IsActive,
            CreatedBy = userId,
            UpdatedBy = userId,
            Fields = writeModel.Fields.Select(ToEntity).ToList()
        };

        dbContext.Set<BarcodeTemplate>().Add(template);
        await dbContext.SaveChangesAsync(cancellationToken);
        return BarcodeTemplateReadModelMapper.ToReadModel(template);
    }

    public async Task<BarcodeTemplateReadModel> UpdateAsync(
        long id,
        BarcodeTemplateWriteModel writeModel,
        BarcodeTemplateAccessScope accessScope,
        long? userId,
        CancellationToken cancellationToken)
    {
        var template = await GetByIdAsync(id, cancellationToken);
        EnsureBusinessMatch(accessScope, template.BusinessId);
        await EnsureNameAvailableAsync(
            template.BusinessId,
            writeModel.Name,
            id,
            cancellationToken);
        if (writeModel.IsDefault)
            await ClearDefaultAsync(template.BusinessId, cancellationToken);

        template.Name = writeModel.Name.Trim();
        template.TemplateType = writeModel.TemplateType;
        template.PaperType = writeModel.PaperType;
        template.LabelWidthMm = writeModel.LabelWidthMm;
        template.LabelHeightMm = writeModel.LabelHeightMm;
        template.IsDefault = writeModel.IsDefault;
        template.IsActive = writeModel.IsActive;
        template.UpdatedBy = userId;
        template.UpdatedAt = DateTime.UtcNow;
        template.Fields.Clear();
        foreach (var field in writeModel.Fields)
            template.Fields.Add(ToEntity(field));

        await dbContext.SaveChangesAsync(cancellationToken);
        return BarcodeTemplateReadModelMapper.ToReadModel(template);
    }

    public async Task SetDefaultAsync(
        long id,
        BarcodeTemplateAccessScope accessScope,
        long? userId,
        CancellationToken cancellationToken)
    {
        var template = await GetByIdAsync(id, cancellationToken);
        EnsureBusinessMatch(accessScope, template.BusinessId);
        await ClearDefaultAsync(template.BusinessId, cancellationToken);
        template.IsDefault = true;
        template.UpdatedBy = userId;
        template.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        long id,
        BarcodeTemplateAccessScope accessScope,
        CancellationToken cancellationToken)
    {
        var template = await GetByIdAsync(id, cancellationToken);
        EnsureBusinessMatch(accessScope, template.BusinessId);
        dbContext.Set<BarcodeTemplate>().Remove(template);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<BarcodeTemplate> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        await dbContext.Set<BarcodeTemplate>()
            .Include(candidate => candidate.Fields.OrderBy(field => field.SortOrder))
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException($"Barcode template with ID {id} not found");

    private async Task EnsureNameAvailableAsync(
        long businessId,
        string name,
        long? excludeId,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToLower();
        var exists = await dbContext.Set<BarcodeTemplate>()
            .AnyAsync(template => template.BusinessId == businessId
                && template.Name.ToLower() == normalizedName
                && (!excludeId.HasValue || template.Id != excludeId.Value),
                cancellationToken);
        if (exists)
            throw new InvalidOperationException($"Barcode template '{name}' already exists");
    }

    private async Task ClearDefaultAsync(long businessId, CancellationToken cancellationToken)
    {
        var templates = await dbContext.Set<BarcodeTemplate>()
            .Where(template => template.BusinessId == businessId && template.IsDefault)
            .ToListAsync(cancellationToken);
        if (templates.Count == 0)
            return;

        foreach (var template in templates)
        {
            template.IsDefault = false;
            template.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureBusinessMatch(BarcodeTemplateAccessScope scope, long businessId)
    {
        if (scope.IsSuperAdmin)
            return;

        if (!scope.BusinessId.HasValue)
            throw new RetailPOS.Application.Authorization.BusinessContextRequiredException();

        if (scope.BusinessId.Value != businessId)
            throw new UnauthorizedAccessException("You are not authorized to access this barcode template.");
    }

    private static BarcodeTemplateField ToEntity(BarcodeTemplateFieldWriteModel field) =>
        new()
        {
            FieldKey = field.FieldKey,
            IsEnabled = field.IsEnabled,
            SortOrder = field.SortOrder,
            X = field.X,
            Y = field.Y,
            Width = field.Width,
            Height = field.Height,
            FontSize = field.FontSize,
            FontWeight = field.FontWeight,
            Align = field.Align
        };
}
