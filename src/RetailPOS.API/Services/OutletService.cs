using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public class OutletService : IOutletService
{
    private readonly IOutletRepository _outletRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<OutletService> _logger;

    public OutletService(
        IOutletRepository outletRepository,
        IUserRepository userRepository,
        ILogger<OutletService> logger)
    {
        _outletRepository = outletRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<OutletDto>> GetAllOutletsAsync()
    {
        var outlets = await _outletRepository.GetAllAsync();
        var outletDtos = new List<OutletDto>();

        foreach (var outlet in outlets)
        {
            var userCount = await _outletRepository.GetUserCountAsync(outlet.Id);
            outletDtos.Add(MapToDto(outlet, userCount));
        }

        return outletDtos;
    }

    public async Task<OutletDto?> GetOutletByIdAsync(long id)
    {
        var outlet = await _outletRepository.GetByIdAsync(id);
        if (outlet == null)
            return null;

        var userCount = await _outletRepository.GetUserCountAsync(id);
        return MapToDto(outlet, userCount);
    }

    public async Task<OutletDto> CreateOutletAsync(CreateOutletDto dto)
    {
        // Validate outlet name uniqueness
        if (await _outletRepository.ExistsByNameAsync(dto.Name))
        {
            _logger.LogWarning("Outlet creation failed: Name '{Name}' already exists", dto.Name);
            throw new InvalidOperationException($"An outlet with the name '{dto.Name}' already exists");
        }

        // Validate manager exists if provided
        if (dto.ManagerId.HasValue)
        {
            var manager = await _userRepository.GetByIdAsync(dto.ManagerId.Value);
            if (manager == null)
            {
                _logger.LogWarning("Outlet creation failed: Manager with ID {ManagerId} not found", dto.ManagerId);
                throw new InvalidOperationException($"Manager with ID {dto.ManagerId} not found");
            }
        }

        var outlet = new Outlet
        {
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
        var outlet = await _outletRepository.GetByIdAsync(id);
        if (outlet == null)
        {
            _logger.LogWarning("Outlet update failed: Outlet with ID {Id} not found", id);
            throw new InvalidOperationException($"Outlet with ID {id} not found");
        }

        // Validate outlet name uniqueness
        if (await _outletRepository.ExistsByNameAsync(dto.Name, id))
        {
            _logger.LogWarning("Outlet update failed: Name '{Name}' already exists", dto.Name);
            throw new InvalidOperationException($"An outlet with the name '{dto.Name}' already exists");
        }

        // Validate manager exists if provided
        if (dto.ManagerId.HasValue)
        {
            var manager = await _userRepository.GetByIdAsync(dto.ManagerId.Value);
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

        var userCount = await _outletRepository.GetUserCountAsync(id);
        return MapToDto(updatedOutlet, userCount);
    }

    public async Task<bool> DeleteOutletAsync(long id)
    {
        var outlet = await _outletRepository.GetByIdAsync(id);
        if (outlet == null)
        {
            _logger.LogWarning("Outlet deletion failed: Outlet with ID {Id} not found", id);
            return false;
        }

        // Check if outlet has users
        var userCount = await _outletRepository.GetUserCountAsync(id);
        if (userCount > 0)
        {
            _logger.LogWarning("Outlet deletion failed: Outlet {Id} has {Count} active users", id, userCount);
            throw new InvalidOperationException($"Cannot delete outlet. It has {userCount} active user(s)");
        }

        var deleted = await _outletRepository.DeleteAsync(id);
        if (deleted)
        {
            _logger.LogInformation("Outlet deleted: ID={Id}, Name={Name}", id, outlet.Name);
        }

        return deleted;
    }

    public async Task<IEnumerable<OutletDto>> SearchOutletsAsync(string searchTerm)
    {
        var outlets = await _outletRepository.SearchAsync(searchTerm);
        var outletDtos = new List<OutletDto>();

        foreach (var outlet in outlets)
        {
            var userCount = await _outletRepository.GetUserCountAsync(outlet.Id);
            outletDtos.Add(MapToDto(outlet, userCount));
        }

        return outletDtos;
    }

    public async Task<OutletStatsDto?> GetOutletStatsAsync(long id)
    {
        var outlet = await _outletRepository.GetByIdAsync(id);
        if (outlet == null)
            return null;

        var userCount = await _outletRepository.GetUserCountAsync(id);

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
