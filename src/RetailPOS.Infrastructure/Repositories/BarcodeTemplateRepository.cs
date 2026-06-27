using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class BarcodeTemplateRepository : IBarcodeTemplateRepository
{
    private readonly RetailPOSDbContext _context;

    public BarcodeTemplateRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<List<BarcodeTemplate>> GetByBusinessAsync(long businessId, bool includeInactive = false)
    {
        var query = _context.Set<BarcodeTemplate>()
            .Include(t => t.Fields)
            .Where(t => t.BusinessId == businessId);

        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<BarcodeTemplate?> GetByIdAsync(long id)
    {
        return await _context.Set<BarcodeTemplate>()
            .Include(t => t.Fields.OrderBy(f => f.SortOrder))
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<BarcodeTemplate> CreateAsync(BarcodeTemplate template)
    {
        _context.Set<BarcodeTemplate>().Add(template);
        await _context.SaveChangesAsync();
        return template;
    }

    public async Task<BarcodeTemplate> UpdateAsync(BarcodeTemplate template)
    {
        template.UpdatedAt = DateTime.UtcNow;
        _context.Set<BarcodeTemplate>().Update(template);
        await _context.SaveChangesAsync();
        return template;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _context.Set<BarcodeTemplate>().FirstOrDefaultAsync(t => t.Id == id);
        if (entity == null) return false;

        _context.Set<BarcodeTemplate>().Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> NameExistsAsync(long businessId, string name, long? excludeId = null)
    {
        var lowered = name.Trim().ToLower();
        return await _context.Set<BarcodeTemplate>()
            .AnyAsync(t => t.BusinessId == businessId
                && t.Name.ToLower() == lowered
                && (!excludeId.HasValue || t.Id != excludeId.Value));
    }

    public async Task ClearDefaultAsync(long businessId)
    {
        var templates = await _context.Set<BarcodeTemplate>()
            .Where(t => t.BusinessId == businessId && t.IsDefault)
            .ToListAsync();

        if (templates.Count == 0)
        {
            return;
        }

        foreach (var template in templates)
        {
            template.IsDefault = false;
            template.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }
}
