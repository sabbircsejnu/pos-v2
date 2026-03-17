using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Sales business logic
/// </summary>
public class SaleService : ISaleService
{
    private readonly ISaleRepository _saleRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<SaleService> _logger;

    public SaleService(
        ISaleRepository saleRepository,
        ICustomerRepository customerRepository,
        RetailPOSDbContext context,
        ILogger<SaleService> logger)
    {
        _saleRepository = saleRepository;
        _customerRepository = customerRepository;
        _context = context;
        _logger = logger;
    }

    /// <summary>Gets a sale by ID</summary>
    public async Task<SaleDto> GetByIdAsync(long id)
    {
        var sale = await _saleRepository.GetByIdAsync(id);
        if (sale == null)
            throw new KeyNotFoundException($"Sale with ID {id} not found");

        return MapToDto(sale);
    }

    /// <summary>Searches sales with filters and pagination</summary>
    public async Task<SaleListDto> SearchAsync(SaleSearchDto searchDto)
    {
        var (sales, totalCount) = await _saleRepository.SearchAsync(
            searchDto.OutletId,
            searchDto.CashierId,
            searchDto.CustomerId,
            searchDto.StartDate,
            searchDto.EndDate,
            searchDto.Status,
            searchDto.PageNumber,
            searchDto.PageSize);

        return new SaleListDto
        {
            Sales = sales.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    /// <summary>Creates a new sale, deducts stock and awards loyalty points</summary>
    public async Task<SaleDto> CreateAsync(CreateSaleDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Sale must have at least one item");

        // Validate customer exists (if provided)
        if (dto.CustomerId.HasValue)
        {
            var customer = await _customerRepository.GetByIdAsync(dto.CustomerId.Value);
            if (customer == null)
                throw new KeyNotFoundException($"Customer with ID {dto.CustomerId.Value} not found");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
        // Check stock and deduct inventory for each item
        foreach (var item in dto.Items)
        {
            var inventory = await _context.Inventories
                .FirstOrDefaultAsync(i =>
                    i.VariantId == item.VariantId &&
                    i.LocationId == dto.OutletId &&
                    i.LocationType == "outlet");

            if (inventory == null)
                throw new InvalidOperationException($"No inventory found for variant ID {item.VariantId} at outlet ID {dto.OutletId}");

            if (inventory.Quantity < item.Quantity)
                throw new InvalidOperationException(
                    $"Insufficient stock for variant ID {item.VariantId}. Available: {inventory.Quantity}, Requested: {item.Quantity}");

            inventory.Quantity -= item.Quantity;
        }

        // Calculate total
        var itemsTotal = dto.Items.Sum(i => i.Quantity * i.UnitPrice);
        var totalAmount = itemsTotal - dto.Discount + dto.Tax;

        var now = DateTime.UtcNow;
        var sale = new Sale
        {
            OutletId = dto.OutletId,
            CustomerId = dto.CustomerId,
            CashierId = dto.CashierId,
            SaleDate = now,
            CreatedAt = now,
            Discount = dto.Discount,
            Tax = dto.Tax,
            TotalAmount = totalAmount,
            PaymentMethod = dto.PaymentMethod.ToLower(),
            Status = "completed",
            Items = dto.Items.Select(i => new SaleItem
            {
                VariantId = i.VariantId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Subtotal = i.Quantity * i.UnitPrice
            }).ToList()
        };

        await _context.Sales.AddAsync(sale);
        await _context.SaveChangesAsync();

        // Award loyalty points (1 point per dollar, rounded down)
        if (dto.CustomerId.HasValue)
        {
            var earnedPoints = (int)Math.Floor(totalAmount);
            if (earnedPoints > 0)
            {
                var customer = await _context.Customers.FindAsync(dto.CustomerId.Value);
                if (customer != null)
                {
                    customer.LoyaltyPoints += earnedPoints;
                    await _context.SaveChangesAsync();
                }
            }
        }

        await transaction.CommitAsync();
        _logger.LogInformation("Sale {SaleId} created for outlet {OutletId}", sale.Id, dto.OutletId);

        return await GetByIdAsync(sale.Id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>Voids a sale (same day only) and restores stock</summary>
    public async Task<SaleDto> VoidAsync(long id, VoidSaleDto dto)
    {
        var sale = await _saleRepository.GetByIdAsync(id);
        if (sale == null)
            throw new KeyNotFoundException($"Sale with ID {id} not found");

        if (sale.Status == "voided")
            throw new InvalidOperationException("Sale is already voided");

        if (sale.Status == "refunded")
            throw new InvalidOperationException("Cannot void a refunded sale");

        // Same-day only check
        if (sale.SaleDate.Date != DateTime.UtcNow.Date)
            throw new InvalidOperationException("Sales can only be voided on the same day");

        using var transaction = await _context.Database.BeginTransactionAsync();

        // Restore inventory
        foreach (var item in sale.Items)
        {
            var inventory = await _context.Inventories
                .FirstOrDefaultAsync(i =>
                    i.VariantId == item.VariantId &&
                    i.LocationId == sale.OutletId &&
                    i.LocationType == "outlet");

            if (inventory != null)
            {
                inventory.Quantity += item.Quantity;
            }
        }

        // Reverse loyalty points if awarded
        if (sale.CustomerId.HasValue)
        {
            var earnedPoints = (int)Math.Floor(sale.TotalAmount);
            if (earnedPoints > 0)
            {
                var customer = await _context.Customers.FindAsync(sale.CustomerId.Value);
                if (customer != null)
                {
                    customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - earnedPoints);
                }
            }
        }

        sale.Status = "voided";
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation("Sale {SaleId} voided. Reason: {Reason}", id, dto.Reason);

        return MapToDto(sale);
    }

    /// <summary>Refunds specific items from a sale and restores their stock</summary>
    public async Task<SaleDto> RefundAsync(long id, RefundSaleDto dto)
    {
        var sale = await _saleRepository.GetByIdAsync(id);
        if (sale == null)
            throw new KeyNotFoundException($"Sale with ID {id} not found");

        if (sale.Status == "voided")
            throw new InvalidOperationException("Cannot refund a voided sale");

        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Refund must include at least one item");

        using var transaction = await _context.Database.BeginTransactionAsync();

        foreach (var refundItem in dto.Items)
        {
            var saleItem = sale.Items.FirstOrDefault(i => i.VariantId == refundItem.VariantId);
            if (saleItem == null)
                throw new InvalidOperationException($"Variant ID {refundItem.VariantId} not found in this sale");

            if (refundItem.Quantity > saleItem.Quantity)
                throw new InvalidOperationException(
                    $"Refund quantity {refundItem.Quantity} exceeds sold quantity {saleItem.Quantity} for variant ID {refundItem.VariantId}");

            // Restore inventory
            var inventory = await _context.Inventories
                .FirstOrDefaultAsync(i =>
                    i.VariantId == refundItem.VariantId &&
                    i.LocationId == sale.OutletId &&
                    i.LocationType == "outlet");

            if (inventory != null)
            {
                inventory.Quantity += refundItem.Quantity;
            }
        }

        sale.Status = "refunded";
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation("Sale {SaleId} refunded. Reason: {Reason}", id, dto.Reason);

        return MapToDto(sale);
    }

    /// <summary>Gets today's sales summary</summary>
    public async Task<SaleSummaryDto> GetTodaysSummaryAsync(long? outletId = null)
    {
        var todaysSales = await _saleRepository.GetTodaysSalesAsync(outletId);
        var salesList = todaysSales.ToList();

        var paymentBreakdown = salesList
            .GroupBy(s => s.PaymentMethod)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalAmount));

        return new SaleSummaryDto
        {
            TotalSales = salesList.Count,
            TotalAmount = salesList.Sum(s => s.TotalAmount),
            TotalDiscount = salesList.Sum(s => s.Discount),
            TotalTax = salesList.Sum(s => s.Tax),
            PaymentBreakdown = paymentBreakdown
        };
    }

    /// <summary>Gets receipt data for a sale</summary>
    public async Task<SaleDto> GetReceiptAsync(long id)
    {
        return await GetByIdAsync(id);
    }

    private static SaleDto MapToDto(Sale s) => new SaleDto
    {
        Id = s.Id,
        OutletId = s.OutletId,
        OutletName = s.Outlet?.Name ?? string.Empty,
        CustomerId = s.CustomerId,
        CustomerName = s.Customer?.Name,
        CashierId = s.CashierId,
        CashierName = s.Cashier?.Name ?? string.Empty,
        SaleDate = s.SaleDate,
        TotalAmount = s.TotalAmount,
        Discount = s.Discount,
        Tax = s.Tax,
        PaymentMethod = s.PaymentMethod,
        Status = s.Status,
        CreatedAt = s.CreatedAt,
        Items = s.Items?.Select(i => new SaleItemDto
        {
            Id = i.Id,
            VariantId = i.VariantId,
            ProductName = i.Variant?.Product?.Name ?? string.Empty,
            VariantSku = i.Variant?.Sku ?? string.Empty,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            Subtotal = i.Subtotal
        }).ToList() ?? new List<SaleItemDto>()
    };
}
