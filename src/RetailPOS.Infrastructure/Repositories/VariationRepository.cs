using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories
{
    public class VariationRepository : IVariationRepository
    {
        private readonly RetailPOSDbContext _context;

        public VariationRepository(RetailPOSDbContext context)
        {
            _context = context;
        }

        public async Task<Variation?> GetByIdAsync(long id)
        {
            return await _context.Set<Variation>()
                .Include(v => v.Options.OrderBy(o => o.DisplayOrder))
                .FirstOrDefaultAsync(v => v.Id == id);
        }

        public async Task<List<Variation>> GetAllAsync(bool includeInactive = false)
        {
            var query = _context.Set<Variation>()
                .Include(v => v.Options.OrderBy(o => o.DisplayOrder))
                .AsQueryable();

            if (!includeInactive)
            {
                query = query.Where(v => v.IsActive);
            }

            return await query.OrderBy(v => v.DisplayOrder).ToListAsync();
        }

        public async Task<Variation> CreateAsync(Variation variation)
        {
            _context.Set<Variation>().Add(variation);
            await _context.SaveChangesAsync();
            return variation;
        }

        public async Task<Variation> UpdateAsync(Variation variation)
        {
            variation.UpdatedAt = DateTime.UtcNow;
            _context.Set<Variation>().Update(variation);
            await _context.SaveChangesAsync();
            return variation;
        }

        public async Task DeleteAsync(long id)
        {
            var variation = await _context.Set<Variation>().FindAsync(id);
            if (variation != null)
            {
                _context.Set<Variation>().Remove(variation);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> NameExistsAsync(string name, long? excludeId = null)
        {
            var query = _context.Set<Variation>().Where(v => v.Name == name);
            if (excludeId.HasValue)
            {
                query = query.Where(v => v.Id != excludeId.Value);
            }
            return await query.AnyAsync();
        }
    }

    public class VariationOptionRepository : IVariationOptionRepository
    {
        private readonly RetailPOSDbContext _context;

        public VariationOptionRepository(RetailPOSDbContext context)
        {
            _context = context;
        }

        public async Task<VariationOption?> GetByIdAsync(long id)
        {
            return await _context.Set<VariationOption>()
                .Include(o => o.Variation)
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        public async Task<List<VariationOption>> GetByVariationIdAsync(long variationId)
        {
            return await _context.Set<VariationOption>()
                .Where(o => o.VariationId == variationId && o.IsActive)
                .OrderBy(o => o.DisplayOrder)
                .ToListAsync();
        }

        public async Task<List<VariationOption>> GetByIdsAsync(List<long> ids)
        {
            return await _context.Set<VariationOption>()
                .Include(o => o.Variation)
                .Where(o => ids.Contains(o.Id))
                .ToListAsync();
        }

        public async Task<VariationOption> CreateAsync(VariationOption option)
        {
            _context.Set<VariationOption>().Add(option);
            await _context.SaveChangesAsync();
            return option;
        }

        public async Task<VariationOption> UpdateAsync(VariationOption option)
        {
            option.UpdatedAt = DateTime.UtcNow;
            _context.Set<VariationOption>().Update(option);
            await _context.SaveChangesAsync();
            return option;
        }

        public async Task DeleteAsync(long id)
        {
            var option = await _context.Set<VariationOption>().FindAsync(id);
            if (option != null)
            {
                _context.Set<VariationOption>().Remove(option);
                await _context.SaveChangesAsync();
            }
        }
    }
}
