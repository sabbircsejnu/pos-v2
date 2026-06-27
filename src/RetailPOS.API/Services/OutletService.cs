using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

public class OutletService : IOutletService
{
    private readonly IOutletRepository _outletRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITenantAccessService _tenantAccess;
    private readonly RetailPOSDbContext _db;
    private readonly ILogger<OutletService> _logger;

    public OutletService(
        IOutletRepository outletRepository,
        IUserRepository userRepository,
        ITenantAccessService tenantAccess,
        RetailPOSDbContext db,
        ILogger<OutletService> logger)
    {
        _outletRepository = outletRepository;
        _userRepository = userRepository;
        _tenantAccess = tenantAccess;
        _db = db;
        _logger = logger;
    }

    public async Task<IEnumerable<OutletDto>> GetAllOutletsAsync()
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var outlets = await _outletRepository.GetAllAsync(businessId);
        var outletDtos = new List<OutletDto>();

        foreach (var outlet in outlets)
        {
            var userCount = await _outletRepository.GetUserCountAsync(outlet.Id, businessId);
            outletDtos.Add(MapToDto(outlet, userCount));
        }

        return outletDtos;
    }

    public async Task<OutletDto?> GetOutletByIdAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var outlet = await _outletRepository.GetByIdAsync(id, businessId);
        if (outlet == null)
            return null;

        var userCount = await _outletRepository.GetUserCountAsync(id, businessId);
        return MapToDto(outlet, userCount);
    }

    public async Task<OutletDto> CreateOutletAsync(CreateOutletDto dto)
    {
        // Validate outlet name uniqueness
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();

        if (await _outletRepository.ExistsByNameAsync(dto.Name, null, businessId))
        {
            _logger.LogWarning("Outlet creation failed: Name '{Name}' already exists", dto.Name);
            throw new InvalidOperationException($"An outlet with the name '{dto.Name}' already exists");
        }

        if (businessId.HasValue)
        {
            var businessLimits = await _db.Businesses
                .AsNoTracking()
                .Where(b => b.Id == businessId.Value)
                .Select(b => new { b.MaxOutlets })
                .FirstOrDefaultAsync();

            if (businessLimits != null && businessLimits.MaxOutlets.HasValue)
            {
                var existingOutlets = await _db.Outlets.LongCountAsync(o => o.BusinessId == businessId.Value);
                if (existingOutlets >= businessLimits.MaxOutlets.Value)
                    throw new InvalidOperationException(
                        $"Outlet limit reached for this business ({businessLimits.MaxOutlets.Value}). Upgrade subscription or increase outlet limit.");
            }
        }

        // Validate manager exists if provided
        if (dto.ManagerId.HasValue)
        {
            var manager = await _userRepository.GetByIdAsync(dto.ManagerId.Value, businessId);
            if (manager == null)
            {
                _logger.LogWarning("Outlet creation failed: Manager with ID {ManagerId} not found", dto.ManagerId);
                throw new InvalidOperationException($"Manager with ID {dto.ManagerId} not found");
            }
        }

        var outlet = new Outlet
        {
            BusinessId = businessId,
            Name = dto.Name,
            Address = dto.Address,
            ContactNumber = dto.ContactNumber,
            ManagerId = dto.ManagerId
        };

        var createdOutlet = await _outletRepository.CreateAsync(outlet);
        _logger.LogInformation("Outlet created: ID={Id}, Name={Name}", createdOutlet.Id, createdOutlet.Name);

        return MapToDto(createdOutlet, 0);
    }

    public async Task<OutletDto> UpdateOutletAsync(long id, UpdateOutletDto dto)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var outlet = await _outletRepository.GetByIdAsync(id, businessId);
        if (outlet == null)
        {
            _logger.LogWarning("Outlet update failed: Outlet with ID {Id} not found", id);
            throw new InvalidOperationException($"Outlet with ID {id} not found");
        }

        // Validate outlet name uniqueness
        if (await _outletRepository.ExistsByNameAsync(dto.Name, id, businessId))
        {
            _logger.LogWarning("Outlet update failed: Name '{Name}' already exists", dto.Name);
            throw new InvalidOperationException($"An outlet with the name '{dto.Name}' already exists");
        }

        // Validate manager exists if provided
        if (dto.ManagerId.HasValue)
        {
            var manager = await _userRepository.GetByIdAsync(dto.ManagerId.Value, businessId);
            if (manager == null)
            {
                _logger.LogWarning("Outlet update failed: Manager with ID {ManagerId} not found", dto.ManagerId);
                throw new InvalidOperationException($"Manager with ID {dto.ManagerId} not found");
            }
        }

        outlet.Name = dto.Name;
        outlet.Address = dto.Address;
        outlet.ContactNumber = dto.ContactNumber;
        outlet.ManagerId = dto.ManagerId;

        var updatedOutlet = await _outletRepository.UpdateAsync(outlet);
        _logger.LogInformation("Outlet updated: ID={Id}, Name={Name}", updatedOutlet.Id, updatedOutlet.Name);

        var userCount = await _outletRepository.GetUserCountAsync(id, businessId);
        return MapToDto(updatedOutlet, userCount);
    }

    public async Task<bool> DeleteOutletAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var outlet = await _outletRepository.GetByIdAsync(id, businessId);
        if (outlet == null)
        {
            _logger.LogWarning("Outlet deletion failed: Outlet with ID {Id} not found", id);
            return false;
        }

        // Check if outlet has users
        var userCount = await _outletRepository.GetUserCountAsync(id, businessId);
        if (userCount > 0)
        {
            _logger.LogWarning("Outlet deletion failed: Outlet {Id} has {Count} active users", id, userCount);
            throw new InvalidOperationException($"Cannot delete outlet. It has {userCount} active user(s)");
        }

        var deleted = await _outletRepository.DeleteAsync(id, businessId);
        if (deleted)
        {
            _logger.LogInformation("Outlet deleted: ID={Id}, Name={Name}", id, outlet.Name);
        }

        return deleted;
    }

    public async Task<IEnumerable<OutletDto>> SearchOutletsAsync(string searchTerm)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var outlets = await _outletRepository.SearchAsync(searchTerm, businessId);
        var outletDtos = new List<OutletDto>();

        foreach (var outlet in outlets)
        {
            var userCount = await _outletRepository.GetUserCountAsync(outlet.Id, businessId);
            outletDtos.Add(MapToDto(outlet, userCount));
        }

        return outletDtos;
    }

    public async Task<OutletStatsDto?> GetOutletStatsAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var outlet = await _outletRepository.GetByIdAsync(id, businessId);
        if (outlet == null)
            return null;

        var userCount = await _outletRepository.GetUserCountAsync(id, businessId);

        // TODO: Calculate sales stats when Sales module is implemented
        return new OutletStatsDto
        {
            Id = outlet.Id,
            Name = outlet.Name,
            UserCount = userCount,
            SalesCount = 0,
            TotalSales = 0
        };
    }

    // ── Super Admin business-scoped operations ────────────────────────────────

    public async Task<IEnumerable<OutletDto>> GetByBusinessIdAsync(long businessId)
    {
        var outlets = await _outletRepository.GetAllAsync(businessId);
        var result = new List<OutletDto>();
        foreach (var outlet in outlets)
        {
            var userCount = await _outletRepository.GetUserCountAsync(outlet.Id, businessId);
            result.Add(MapToDto(outlet, userCount));
        }
        return result;
    }

    public async Task<OutletDto> CreateForBusinessAsync(long businessId, CreateOutletDto dto)
    {
        if (await _outletRepository.ExistsByNameAsync(dto.Name, null, businessId))
            throw new InvalidOperationException($"An outlet with the name '{dto.Name}' already exists in this business");

        var businessLimits = await _db.Businesses
            .AsNoTracking()
            .Where(b => b.Id == businessId)
            .Select(b => new { b.MaxOutlets })
            .FirstOrDefaultAsync();

        if (businessLimits == null)
            throw new InvalidOperationException($"Business {businessId} not found");

        if (businessLimits.MaxOutlets.HasValue)
        {
            var count = await _db.Outlets.LongCountAsync(o => o.BusinessId == businessId);
            if (count >= businessLimits.MaxOutlets.Value)
                throw new InvalidOperationException(
                    $"Outlet limit reached ({businessLimits.MaxOutlets.Value}). Upgrade subscription or increase outlet limit.");
        }

        if (dto.ManagerId.HasValue)
        {
            var manager = await _userRepository.GetByIdAsync(dto.ManagerId.Value, businessId);
            if (manager == null)
                throw new InvalidOperationException($"Manager with ID {dto.ManagerId} not found in this business");
        }

        var outlet = new Outlet
        {
            BusinessId = businessId,
            Name = dto.Name,
            Address = dto.Address,
            ContactNumber = dto.ContactNumber,
            ManagerId = dto.ManagerId
        };

        var created = await _outletRepository.CreateAsync(outlet);
        _logger.LogInformation("Business setup: Outlet created for business {BusinessId}: ID={Id}, Name={Name}", businessId, created.Id, created.Name);
        return MapToDto(created, 0);
    }

    public async Task<OutletDto> UpdateForBusinessAsync(long businessId, long id, UpdateOutletDto dto)
    {
        var outlet = await _outletRepository.GetByIdAsync(id, businessId);
        if (outlet == null)
            throw new InvalidOperationException($"Outlet {id} not found in business {businessId}");

        if (await _outletRepository.ExistsByNameAsync(dto.Name, id, businessId))
            throw new InvalidOperationException($"An outlet with the name '{dto.Name}' already exists in this business");

        if (dto.ManagerId.HasValue)
        {
            var manager = await _userRepository.GetByIdAsync(dto.ManagerId.Value, businessId);
            if (manager == null)
                throw new InvalidOperationException($"Manager with ID {dto.ManagerId} not found in this business");
        }

        outlet.Name = dto.Name;
        outlet.Address = dto.Address;
        outlet.ContactNumber = dto.ContactNumber;
        outlet.ManagerId = dto.ManagerId;

        var updated = await _outletRepository.UpdateAsync(outlet);
        _logger.LogInformation("Business setup: Outlet updated for business {BusinessId}: ID={Id}", businessId, updated.Id);

        var userCount = await _outletRepository.GetUserCountAsync(id, businessId);
        return MapToDto(updated, userCount);
    }

    public async Task<bool> DeleteForBusinessAsync(long businessId, long id)
    {
        var outlet = await _outletRepository.GetByIdAsync(id, businessId);
        if (outlet == null)
            return false;

        var userCount = await _outletRepository.GetUserCountAsync(id, businessId);
        if (userCount > 0)
            throw new InvalidOperationException($"Cannot delete outlet. It has {userCount} active user(s)");

        var deleted = await _outletRepository.DeleteAsync(id, businessId);
        if (deleted)
            _logger.LogInformation("Business setup: Outlet deleted from business {BusinessId}: ID={Id}", businessId, id);
        return deleted;
    }

    private static OutletDto MapToDto(Outlet outlet, int userCount)
    {
        return new OutletDto
        {
            Id = outlet.Id,
            Name = outlet.Name,
            Address = outlet.Address,
            ContactNumber = outlet.ContactNumber,
            ManagerId = outlet.ManagerId,
            ManagerName = outlet.Manager?.Name,
            UserCount = userCount,
            CreatedAt = outlet.CreatedAt,
            UpdatedAt = outlet.UpdatedAt
        };
    }
}
