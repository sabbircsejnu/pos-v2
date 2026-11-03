using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Barcodes;

public sealed class BarcodeTemplateReadStore(RetailPOSDbContext dbContext)
    : IBarcodeTemplateReadStore
{
    public async Task<IReadOnlyList<BarcodeTemplateReadModel>> GetByBusinessAsync(
        long businessId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Set<BarcodeTemplate>()
            .AsNoTracking()
            .Include(template => template.Fields)
            .Where(template => template.BusinessId == businessId);

        if (!includeInactive)
            query = query.Where(template => template.IsActive);

        var templates = await query
            .OrderByDescending(template => template.IsDefault)
            .ThenBy(template => template.Name)
            .ToListAsync(cancellationToken);

        return templates.Select(BarcodeTemplateReadModelMapper.ToReadModel).ToArray();
    }

    public async Task<BarcodeTemplateLookupReadModel> GetByIdAsync(
        long id,
        BarcodeTemplateAccessScope accessScope,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.Set<BarcodeTemplate>()
            .AsNoTracking()
            .Include(candidate => candidate.Fields.OrderBy(field => field.SortOrder))
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
            return new BarcodeTemplateLookupReadModel(BarcodeTemplateLookupStatus.NotFound, null);

        if (!accessScope.IsSuperAdmin && !accessScope.BusinessId.HasValue)
            return new BarcodeTemplateLookupReadModel(
                BarcodeTemplateLookupStatus.BusinessScopeRequired,
                null);

        if (!accessScope.IsSuperAdmin && template.BusinessId != accessScope.BusinessId)
            return new BarcodeTemplateLookupReadModel(BarcodeTemplateLookupStatus.Forbidden, null);

        return new BarcodeTemplateLookupReadModel(
            BarcodeTemplateLookupStatus.Found,
            BarcodeTemplateReadModelMapper.ToReadModel(template));
    }
}
