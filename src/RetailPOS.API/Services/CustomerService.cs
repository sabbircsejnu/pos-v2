using RetailPOS.API.DTOs.Customer;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Customer business logic
/// </summary>
public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(ICustomerRepository customerRepository, ILogger<CustomerService> logger)
    {
        _customerRepository = customerRepository;
        _logger = logger;
    }

    /// <summary>Gets a customer by ID</summary>
    public async Task<CustomerDto> GetByIdAsync(long id)
    {
        var customer = await _customerRepository.GetByIdAsync(id);
        if (customer == null)
            throw new KeyNotFoundException($"Customer with ID {id} not found");

        return MapToDto(customer);
    }

    /// <summary>Searches customers with pagination</summary>
    public async Task<CustomerListDto> SearchAsync(CustomerSearchDto searchDto)
    {
        var (customers, totalCount) = await _customerRepository.SearchAsync(
            searchDto.Query,
            searchDto.PageNumber,
            searchDto.PageSize);

        return new CustomerListDto
        {
            Customers = customers.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    /// <summary>Quick search for POS autocomplete (top 10)</summary>
    public async Task<IEnumerable<CustomerDto>> QuickSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Enumerable.Empty<CustomerDto>();

        var customers = await _customerRepository.QuickSearchAsync(query, 10);
        return customers.Select(MapToDto);
    }

    /// <summary>Creates a new customer</summary>
    public async Task<CustomerDto> CreateAsync(CreateCustomerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("Customer name is required");

        // Check phone uniqueness
        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            var existing = await _customerRepository.GetByPhoneAsync(dto.Phone);
            if (existing != null)
                throw new InvalidOperationException($"A customer with phone '{dto.Phone}' already exists");
        }

        // Check email uniqueness
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var existing = await _customerRepository.GetByEmailAsync(dto.Email);
            if (existing != null)
                throw new InvalidOperationException($"A customer with email '{dto.Email}' already exists");
        }

        var customer = new Customer
        {
            Name = dto.Name.Trim(),
            CustomerCode = null,
            IsSystem = false,
            IsActive = true,
            Phone = dto.Phone?.Trim(),
            Email = dto.Email?.Trim()?.ToLower(),
            LoyaltyPoints = 0
        };

        var created = await _customerRepository.CreateAsync(customer);
        _logger.LogInformation("Customer {CustomerId} created", created.Id);

        return MapToDto(created);
    }

    /// <summary>Updates an existing customer</summary>
    public async Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto dto)
    {
        var customer = await _customerRepository.GetByIdAsync(id);
        if (customer == null)
            throw new KeyNotFoundException($"Customer with ID {id} not found");

        if (customer.IsSystem)
            throw new InvalidOperationException("System customer cannot be edited");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("Customer name is required");

        // Check phone uniqueness (exclude current)
        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            var existing = await _customerRepository.GetByPhoneAsync(dto.Phone);
            if (existing != null && existing.Id != id)
                throw new InvalidOperationException($"A customer with phone '{dto.Phone}' already exists");
        }

        // Check email uniqueness (exclude current)
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var existing = await _customerRepository.GetByEmailAsync(dto.Email);
            if (existing != null && existing.Id != id)
                throw new InvalidOperationException($"A customer with email '{dto.Email}' already exists");
        }

        customer.Name = dto.Name.Trim();
        customer.Phone = dto.Phone?.Trim();
        customer.Email = dto.Email?.Trim()?.ToLower();

        var updated = await _customerRepository.UpdateAsync(customer);
        _logger.LogInformation("Customer {CustomerId} updated", id);

        return MapToDto(updated);
    }

    /// <summary>Deletes a customer</summary>
    public async Task<bool> DeleteAsync(long id)
    {
        var customer = await _customerRepository.GetByIdAsync(id);
        if (customer == null)
            throw new KeyNotFoundException($"Customer with ID {id} not found");

        if (customer.IsSystem)
            throw new InvalidOperationException("System customer cannot be deleted");

        var result = await _customerRepository.DeleteAsync(id);
        if (result)
            _logger.LogInformation("Customer {CustomerId} deleted", id);

        return result;
    }

    /// <summary>Adds loyalty points to a customer</summary>
    public async Task<CustomerDto> AddLoyaltyPointsAsync(long id, int points)
    {
        if (points <= 0)
            throw new InvalidOperationException("Points must be greater than zero");

        var customer = await _customerRepository.GetByIdAsync(id);
        if (customer == null)
            throw new KeyNotFoundException($"Customer with ID {id} not found");

        customer.LoyaltyPoints += points;
        var updated = await _customerRepository.UpdateAsync(customer);
        _logger.LogInformation("Added {Points} loyalty points to customer {CustomerId}", points, id);

        return MapToDto(updated);
    }

    /// <summary>Redeems loyalty points from a customer</summary>
    public async Task<CustomerDto> RedeemLoyaltyPointsAsync(long id, int points)
    {
        if (points <= 0)
            throw new InvalidOperationException("Points must be greater than zero");

        var customer = await _customerRepository.GetByIdAsync(id);
        if (customer == null)
            throw new KeyNotFoundException($"Customer with ID {id} not found");

        if (customer.LoyaltyPoints < points)
            throw new InvalidOperationException($"Insufficient loyalty points. Customer has {customer.LoyaltyPoints} points but {points} were requested");

        customer.LoyaltyPoints -= points;
        var updated = await _customerRepository.UpdateAsync(customer);
        _logger.LogInformation("Redeemed {Points} loyalty points from customer {CustomerId}", points, id);

        return MapToDto(updated);
    }

    private static CustomerDto MapToDto(Customer c) => new CustomerDto
    {
        Id = c.Id,
        Name = c.Name,
        Phone = c.Phone,
        Email = c.Email,
        LoyaltyPoints = c.LoyaltyPoints,
        CreatedAt = c.CreatedAt,
        TotalPurchases = c.Sales?.Count ?? 0
    };
}
