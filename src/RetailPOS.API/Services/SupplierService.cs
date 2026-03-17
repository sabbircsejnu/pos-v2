using RetailPOS.API.DTOs.Supplier;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public class SupplierService : ISupplierService
{
    private readonly ISupplierRepository _supplierRepo;
    private readonly ILogger<SupplierService> _logger;

    public SupplierService(
        ISupplierRepository supplierRepo,
        ILogger<SupplierService> logger)
    {
        _supplierRepo = supplierRepo;
        _logger = logger;
    }

    public async Task<List<SupplierDto>> GetAllSuppliersAsync()
    {
        var suppliers = await _supplierRepo.GetAllAsync();
        return suppliers.Select(MapToDto).ToList();
    }

    public async Task<SupplierListDto> SearchSuppliersAsync(SupplierSearchDto searchDto)
    {
        var (suppliers, totalCount) = await _supplierRepo.SearchAsync(
            searchDto.SearchQuery,
            searchDto.MinCreditLimit,
            searchDto.MaxCreditLimit,
            searchDto.PageNumber,
            searchDto.PageSize,
            searchDto.SortBy,
            searchDto.SortOrder);

        return new SupplierListDto
        {
            Suppliers = suppliers.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    public async Task<SupplierDto> GetSupplierByIdAsync(long id)
    {
        var supplier = await _supplierRepo.GetByIdAsync(id);
        if (supplier == null)
            throw new KeyNotFoundException($"Supplier with ID {id} not found");

        return MapToDto(supplier);
    }

    public async Task<SupplierDto> CreateSupplierAsync(CreateSupplierDto dto)
    {
        // Validate name uniqueness
        if (await _supplierRepo.NameExistsAsync(dto.Name))
            throw new InvalidOperationException($"Supplier with name '{dto.Name}' already exists");

        var supplier = new Supplier
        {
            Name = dto.Name,
            Contact = dto.Contact,
            Address = dto.Address,
            CreditLimit = dto.CreditLimit
        };

        supplier = await _supplierRepo.CreateAsync(supplier);
        _logger.LogInformation("Supplier created: {SupplierName} (ID: {SupplierId})", supplier.Name, supplier.Id);

        return MapToDto(supplier);
    }

    public async Task<SupplierDto> UpdateSupplierAsync(long id, UpdateSupplierDto dto)
    {
        var supplier = await _supplierRepo.GetByIdAsync(id);
        if (supplier == null)
            throw new KeyNotFoundException($"Supplier with ID {id} not found");

        // Validate name uniqueness (excluding current supplier)
        if (await _supplierRepo.NameExistsAsync(dto.Name, id))
            throw new InvalidOperationException($"Supplier with name '{dto.Name}' already exists");

        supplier.Name = dto.Name;
        supplier.Contact = dto.Contact;
        supplier.Address = dto.Address;
        supplier.CreditLimit = dto.CreditLimit;

        supplier = await _supplierRepo.UpdateAsync(supplier);
        _logger.LogInformation("Supplier updated: {SupplierName} (ID: {SupplierId})", supplier.Name, supplier.Id);

        return MapToDto(supplier);
    }

    public async Task DeleteSupplierAsync(long id)
    {
        var supplier = await _supplierRepo.GetByIdAsync(id);
        if (supplier == null)
            throw new KeyNotFoundException($"Supplier with ID {id} not found");

        // Check if supplier has purchase orders or bills
        var totalPOs = await _supplierRepo.GetTotalPurchaseOrdersAsync(id);
        var totalBills = await _supplierRepo.GetTotalBillsAsync(id);

        if (totalPOs > 0 || totalBills > 0)
            throw new InvalidOperationException(
                $"Cannot delete supplier '{supplier.Name}'. It has {totalPOs} purchase orders and {totalBills} bills associated with it.");

        var deleted = await _supplierRepo.DeleteAsync(id);
        if (!deleted)
            throw new Exception("Failed to delete supplier");

        _logger.LogInformation("Supplier deleted: {SupplierName} (ID: {SupplierId})", supplier.Name, id);
    }

    public async Task<SupplierPerformanceDto> GetSupplierPerformanceAsync(long id)
    {
        var supplier = await _supplierRepo.GetByIdAsync(id);
        if (supplier == null)
            throw new KeyNotFoundException($"Supplier with ID {id} not found");

        var totalPOs = await _supplierRepo.GetTotalPurchaseOrdersAsync(id);
        var totalBills = await _supplierRepo.GetTotalBillsAsync(id);
        var totalPurchaseAmount = await _supplierRepo.GetTotalPurchaseAmountAsync(id);
        var outstandingBalance = await _supplierRepo.GetOutstandingBalanceAsync(id);

        var creditUtilization = supplier.CreditLimit > 0
            ? (outstandingBalance / supplier.CreditLimit) * 100
            : 0;

        var healthStatus = creditUtilization switch
        {
            >= 90 => "Critical",
            >= 70 => "Warning",
            _ => "Good"
        };

        return new SupplierPerformanceDto
        {
            Id = supplier.Id,
            Name = supplier.Name,
            TotalPurchaseOrders = totalPOs,
            TotalBills = totalBills,
            TotalPurchaseAmount = totalPurchaseAmount,
            OutstandingBalance = outstandingBalance,
            CreditLimit = supplier.CreditLimit,
            CreditUtilizationPercentage = creditUtilization,
            HealthStatus = healthStatus
        };
    }

    private static SupplierDto MapToDto(Supplier supplier)
    {
        return new SupplierDto
        {
            Id = supplier.Id,
            Name = supplier.Name,
            Contact = supplier.Contact,
            Address = supplier.Address,
            CreditLimit = supplier.CreditLimit,
            CreatedAt = supplier.CreatedAt,
            UpdatedAt = supplier.UpdatedAt
        };
    }
}
