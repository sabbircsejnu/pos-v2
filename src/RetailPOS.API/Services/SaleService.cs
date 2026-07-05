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
    private readonly IStockLedgerService _stockLedger;
    private readonly IPosCacheService    _posCache;
    private readonly ISaleEventPublisher _eventPublisher;  // UPDATED
    private readonly ISettingsService _settingsService;

    public SaleService(
        ISaleRepository      saleRepository,
        ICustomerRepository  customerRepository,
        RetailPOSDbContext   context,
        ILogger<SaleService> logger,
        IStockLedgerService  stockLedger,
        IPosCacheService     posCache,
        ISaleEventPublisher  eventPublisher,  // UPDATED
        ISettingsService     settingsService)
    {
        _saleRepository     = saleRepository;
        _customerRepository = customerRepository;
        _context            = context;
        _logger             = logger;
        _posCache           = posCache;
        _stockLedger        = stockLedger;
        _eventPublisher     = eventPublisher; // UPDATED
        _settingsService    = settingsService;
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

    /// <summary>
    /// Creates a new sale, deducts stock, awards loyalty points, writes ledger entries,
    /// and publishes the SaleCompleted event.
    /// Prevents duplicate submission via the optional IdempotencyKey field.
    /// </summary>
    public async Task<SaleDto> CreateAsync(CreateSaleDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Sale must have at least one item");

        // UPDATED â€” idempotency check: return existing sale if key was already used
        if (!string.IsNullOrWhiteSpace(dto.IdempotencyKey))
        {
            var existing = await _saleRepository.FindByIdempotencyKeyAsync(dto.OutletId, dto.IdempotencyKey);
            if (existing != null)
            {
                _logger.LogInformation(
                    "Duplicate submission detected for IdempotencyKey={Key}, returning existing Sale {SaleId}",
                    dto.IdempotencyKey, existing.Id);
                return MapToDto(existing);
            }
        }

        // Resolve customer: explicit selection or default Walk-in customer.
        long? resolvedCustomerId = dto.CustomerId;
        Customer? resolvedCustomer = null;

        if (resolvedCustomerId.HasValue)
        {
            resolvedCustomer = await _customerRepository.GetByIdAsync(resolvedCustomerId.Value);
            if (resolvedCustomer == null)
                throw new KeyNotFoundException($"Customer with ID {resolvedCustomerId.Value} not found");

            if (!resolvedCustomer.IsActive)
                throw new InvalidOperationException($"Customer with ID {resolvedCustomerId.Value} is inactive");
        }
        else
        {
            resolvedCustomer = await _customerRepository.GetByCodeAsync(Customer.WalkInCustomerCode);
            if (resolvedCustomer == null)
                throw new InvalidOperationException("Walk-in Customer is not configured. Seed a system customer with code WALKIN.");

            if (!resolvedCustomer.IsActive)
                throw new InvalidOperationException("Walk-in Customer is inactive. Activate the WALKIN customer record.");

            resolvedCustomerId = resolvedCustomer.Id;
        }

        // UPDATED â€” validate payment split sums to total (when split payments supplied)
        if (dto.Payments.Count > 0)
        {
            var itemsTotal = dto.Items.Sum(i => i.Quantity * i.UnitPrice - i.DiscountAmount);
            var netTotal   = itemsTotal - dto.Discount + dto.Tax;
            var paidTotal  = dto.Payments.Sum(p => p.Amount);
            if (Math.Abs(paidTotal - netTotal) > 0.01m)
                throw new InvalidOperationException(
                    $"Payment total {paidTotal:F2} does not match sale total {netTotal:F2}. " +
                    "Check payment split amounts.");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Check stock and deduct inventory for each item
            var inventoryLedgerMap = new List<(Inventory Inventory, CreateSaleItemDto Item)>();

            foreach (var item in dto.Items)
            {
                var inventory = await _context.Inventories
                    .FirstOrDefaultAsync(i =>
                        i.VariantId   == item.VariantId &&
                        i.LocationId  == dto.OutletId &&
                        i.LocationType == "outlet");

                if (inventory == null)
                    throw new InvalidOperationException(
                        $"No inventory found for variant ID {item.VariantId} at outlet ID {dto.OutletId}");

                if (inventory.Quantity < item.Quantity)
                    throw new InvalidOperationException(
                        $"Insufficient stock for variant ID {item.VariantId}. " +
                        $"Available: {inventory.Quantity}, Requested: {item.Quantity}");

                inventory.Quantity -= item.Quantity;
                inventoryLedgerMap.Add((inventory, item));
            }

            // Calculate totals
            var lineItemsTotal = dto.Items.Sum(i => i.Quantity * i.UnitPrice - i.DiscountAmount);
            var totalAmount    = lineItemsTotal - dto.Discount + dto.Tax;

            // UPDATED â€” determine primary payment method
            var primaryMethod = dto.Payments.Count > 0
                ? dto.Payments.OrderByDescending(p => p.Amount).First().Method.ToLower()
                : dto.PaymentMethod.ToLower();

            var now        = DateTime.UtcNow;
            var saleDate   = dto.SalesDate ?? now;
            var terminalId = await ResolveTerminalIdAsync(dto.OutletId, dto.TerminalId);
            if (!terminalId.HasValue)
            {
                throw new InvalidOperationException(
                    $"No active terminal is configured for outlet {dto.OutletId}. Select a terminal before completing the sale.");
            }
            var saleNumber = await _settingsService.AllocateNextInvoiceNumberAsync(saleDate);  // UPDATED

            var sale = new Sale
            {
                SaleNumber     = saleNumber,             // UPDATED
                OutletId       = dto.OutletId,
                TerminalId     = terminalId,
                CustomerId     = resolvedCustomerId,
                CashierId      = dto.CashierId,
                SaleDate       = saleDate,
                CreatedAt      = now,
                Discount       = dto.Discount,
                Tax            = dto.Tax,
                TotalAmount    = totalAmount,
                PaymentMethod  = primaryMethod,
                Status         = "completed",
                IdempotencyKey = dto.IdempotencyKey,     // UPDATED
                Items = dto.Items.Select(i => new SaleItem
                {
                    VariantId      = i.VariantId,
                    Quantity       = i.Quantity,
                    UnitPrice      = i.UnitPrice,
                    Subtotal       = i.Quantity * i.UnitPrice - i.DiscountAmount,
                    DiscountAmount = i.DiscountAmount,    // UPDATED
                    AppliedRuleId  = i.AppliedRuleId,     // UPDATED
                    AppliedRuleName = i.AppliedRuleName   // UPDATED
                }).ToList()
            };

            // UPDATED â€” build payment rows (split or single)
            if (dto.Payments.Count > 0)
            {
                sale.Payments = dto.Payments.Select(p => new SalePayment
                {
                    Method   = p.Method.ToLower(),
                    Amount   = p.Amount,
                    Tendered = p.Tendered
                }).ToList();
            }
            else
            {
                sale.Payments = new List<SalePayment>
                {
                    new SalePayment
                    {
                        Method   = primaryMethod,
                        Amount   = totalAmount,
                        Tendered = primaryMethod == "cash" ? dto.Payments.FirstOrDefault()?.Tendered : null
                    }
                };
            }

            await _context.Sales.AddAsync(sale);
            await _context.SaveChangesAsync(); // sale.Id is now populated

            // Write ledger entries now that sale.Id is known
            foreach (var (inventory, item) in inventoryLedgerMap)
            {
                _stockLedger.WriteEntry(
                    variantId: item.VariantId,
                    locationId: dto.OutletId,
                    locationType: "outlet",
                    transactionType: StockLedgerTransactionType.Sale,
                    qtyIn: 0,
                    qtyOut: item.Quantity,
                    balanceAfter: inventory.Quantity,
                    referenceType: StockLedgerReferenceType.Sale,
                    referenceId: sale.Id,
                    createdBy: dto.CashierId,
                    createdAt: saleDate);
            }

            // Award loyalty points (1 point per currency unit, rounded down)
            int earnedPoints = 0;
            if (resolvedCustomerId.HasValue && !(resolvedCustomer?.IsSystem ?? false))
            {
                earnedPoints = (int)Math.Floor(totalAmount);
                if (earnedPoints > 0)
                {
                    var customer = await _context.Customers.FindAsync(resolvedCustomerId.Value);
                    if (customer != null)
                    {
                        customer.LoyaltyPoints += earnedPoints;
                    }
                }
            }

            // xmin optimistic concurrency token on Inventory throws DbUpdateConcurrencyException
            // if another transaction modified the same row between our read and this write.
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException(
                    "Stock was modified by another transaction. Please retry the sale.");
            }

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Sale {SaleNumber} (ID={SaleId}) created for outlet {OutletId}",
                sale.SaleNumber, sale.Id, dto.OutletId);

            // Invalidate per-variant stock hint cache so the next POS scan is fresh
            foreach (var item in dto.Items)
                await _posCache.InvalidateStockAsync(item.VariantId, dto.OutletId);

            // UPDATED â€” publish event (fire-and-forget; never block response)
            _ = _eventPublisher.PublishSaleCompletedAsync(new SaleCompletedEvent
            {
                SaleId              = sale.Id,
                SaleNumber          = sale.SaleNumber,
                OutletId            = dto.OutletId,
                CashierId           = dto.CashierId,
                CustomerId          = resolvedCustomerId,
                TotalAmount         = totalAmount,
                CompletedAt         = now,
                LoyaltyPointsAwarded = earnedPoints
            });

            return await GetByIdAsync(sale.Id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>Voids a completed sale and restores stock</summary>
    public async Task<SaleDto> VoidAsync(long id, VoidSaleDto dto, long? voidedByUserId = null)
    {
        var sale = await _saleRepository.GetByIdAsync(id);
        if (sale == null)
            throw new KeyNotFoundException($"Sale with ID {id} not found");

        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new InvalidOperationException("Void reason is required");

        if (sale.Status == "voided")
            throw new InvalidOperationException("Sale is already voided");

        if (sale.Status == "refunded")
            throw new InvalidOperationException("Cannot void a refunded sale");

        if (!string.Equals(sale.Status, "completed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only completed sales can be voided");

        var previousStatus = sale.Status;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Restore inventory and track for ledger
            var inventoryLedgerMap = new List<(Inventory Inventory, SaleItem Item)>();

            foreach (var item in sale.Items)
            {
                var inventory = await _context.Inventories
                    .FirstOrDefaultAsync(i =>
                        i.VariantId   == item.VariantId &&
                        i.LocationId  == sale.OutletId &&
                        i.LocationType == "outlet");

                if (inventory != null)
                {
                    inventory.Quantity += item.Quantity;
                    inventoryLedgerMap.Add((inventory, item));
                }
            }

            // Write ledger entries (void return rows)
            foreach (var (inventory, item) in inventoryLedgerMap)
            {
                _stockLedger.WriteEntry(
                    variantId: item.VariantId,
                    locationId: sale.OutletId,
                    locationType: "outlet",
                    transactionType: StockLedgerTransactionType.Return,
                    qtyIn: item.Quantity,
                    qtyOut: 0,
                    balanceAfter: inventory.Quantity,
                    referenceType: StockLedgerReferenceType.Sale,
                    referenceId: sale.Id,
                    remarks: $"Sale voided â€” {dto.Reason}",
                    createdBy: null);
            }

            // Reverse loyalty points if awarded
            if (sale.CustomerId.HasValue)
            {
                var earnedPoints = (int)Math.Floor(sale.TotalAmount);
                if (earnedPoints > 0)
                {
                    var customer = await _context.Customers.FindAsync(sale.CustomerId.Value);
                    if (customer != null && !customer.IsSystem)
                    {
                        customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - earnedPoints);
                    }
                }
            }

            _context.SaleVoids.Add(new SaleVoid
            {
                SaleId = sale.Id,
                VoidedByUserId = voidedByUserId,
                PreviousStatus = previousStatus,
                Reason = dto.Reason.Trim(),
                VoidedAt = DateTime.UtcNow
            });

            sale.Status = "voided";
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Invalidate stock hint cache for each restored item
            foreach (var item in sale.Items)
                await _posCache.InvalidateStockAsync(item.VariantId, sale.OutletId);

            _logger.LogInformation("Sale {SaleId} voided. Reason: {Reason}", id, dto.Reason);

            return MapToDto(sale);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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
        try
        {
            // Restore inventory and track for ledger
            var inventoryLedgerMap = new List<(Inventory Inventory, int Qty, long VariantId)>();

            foreach (var refundItem in dto.Items)
            {
                var saleItem = sale.Items.FirstOrDefault(i => i.VariantId == refundItem.VariantId);
                if (saleItem == null)
                    throw new InvalidOperationException(
                        $"Variant ID {refundItem.VariantId} not found in this sale");

                if (refundItem.Quantity > saleItem.Quantity)
                    throw new InvalidOperationException(
                        $"Refund quantity {refundItem.Quantity} exceeds sold quantity {saleItem.Quantity} " +
                        $"for variant ID {refundItem.VariantId}");

                var inventory = await _context.Inventories
                    .FirstOrDefaultAsync(i =>
                        i.VariantId   == refundItem.VariantId &&
                        i.LocationId  == sale.OutletId &&
                        i.LocationType == "outlet");

                if (inventory != null)
                {
                    inventory.Quantity += refundItem.Quantity;
                    inventoryLedgerMap.Add((inventory, refundItem.Quantity, refundItem.VariantId));
                }
            }

            // Write ledger entries (refund return rows)
            foreach (var (inventory, qty, variantId) in inventoryLedgerMap)
            {
                _stockLedger.WriteEntry(
                    variantId: variantId,
                    locationId: sale.OutletId,
                    locationType: "outlet",
                    transactionType: StockLedgerTransactionType.Return,
                    qtyIn: qty,
                    qtyOut: 0,
                    balanceAfter: inventory.Quantity,
                    referenceType: StockLedgerReferenceType.Sale,
                    referenceId: sale.Id,
                    remarks: $"Sale refunded â€” {dto.Reason}",
                    createdBy: null);
            }

            sale.Status = "refunded";
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Invalidate stock hint cache for each refunded item
            foreach (var (_, _, variantId) in inventoryLedgerMap)
                await _posCache.InvalidateStockAsync(variantId, sale.OutletId);

            _logger.LogInformation("Sale {SaleId} refunded. Reason: {Reason}", id, dto.Reason);

            return MapToDto(sale);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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
    public async Task<SaleDto> GetReceiptAsync(long id, long? printedByUserId = null)
    {
        var sale = await _saleRepository.GetByIdAsync(id);
        if (sale == null)
            throw new KeyNotFoundException($"Sale with ID {id} not found");

        await TrackReceiptHistoryAsync(sale, "print", printedByUserId);
        return MapToDto(sale);
    }

    /// <summary>Gets printable payload for receipt reprint of a completed sale</summary>
    public async Task<SaleDto> ReprintReceiptAsync(long id, long? printedByUserId = null)
    {
        var sale = await _saleRepository.GetByIdAsync(id);
        if (sale == null)
            throw new KeyNotFoundException($"Sale with ID {id} not found");

        if (!string.Equals(sale.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sale.Status, "voided", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sale.Status, "refunded", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only finalized sales can be reprinted");
        }

        await TrackReceiptHistoryAsync(sale, "reprint", printedByUserId);
        return MapToDto(sale);
    }

    private async Task TrackReceiptHistoryAsync(Sale sale, string actionType, long? printedByUserId)
    {
        _context.ReceiptPrintHistories.Add(new ReceiptPrintHistory
        {
            SaleId = sale.Id,
            TerminalId = sale.TerminalId,
            PrintedByUserId = printedByUserId,
            ActionType = actionType,
            PrintedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    private async Task<long?> ResolveTerminalIdAsync(long outletId, long? requestedTerminalId)
    {
        if (requestedTerminalId.HasValue)
        {
            var terminal = await _context.PosTerminals
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == requestedTerminalId.Value && t.OutletId == outletId && t.IsActive);

            if (terminal == null)
                throw new InvalidOperationException($"Terminal {requestedTerminalId.Value} is not active for outlet {outletId}.");

            return terminal.Id;
        }

        var defaultTerminal = await _context.PosTerminals
            .AsNoTracking()
            .Where(t => t.OutletId == outletId && t.IsActive)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Id)
            .FirstOrDefaultAsync();

        return defaultTerminal?.Id;
    }

    private static SaleDto MapToDto(Sale s)
    {
        var netTotal = s.TotalAmount - s.Discount + s.Tax;  // UPDATED

        return new SaleDto
        {
            Id          = s.Id,
            SaleNumber  = s.SaleNumber,                     // UPDATED
            OutletId    = s.OutletId,
            OutletName  = s.Outlet?.Name ?? string.Empty,
            TerminalId = s.TerminalId,
            TerminalName = s.Terminal?.Name,
            CustomerId  = s.CustomerId,
            CustomerName = s.Customer?.Name,
            CashierId   = s.CashierId,
            CashierName = s.Cashier?.Name ?? string.Empty,
            SaleDate    = s.SaleDate,
            TotalAmount = s.TotalAmount,
            Discount    = s.Discount,
            Tax         = s.Tax,
            NetTotal    = netTotal,                          // UPDATED
            PaymentMethod = s.PaymentMethod,
            Status      = s.Status,
            CreatedAt   = s.CreatedAt,
            Items = s.Items?.Select(i => new SaleItemDto
            {
                Id             = i.Id,
                VariantId      = i.VariantId,
                ProductName    = i.Variant?.Product?.Name ?? string.Empty,
                VariantSku     = i.Variant?.Sku ?? string.Empty,
                Quantity       = i.Quantity,
                UnitPrice      = i.UnitPrice,
                DiscountAmount = i.DiscountAmount,           // UPDATED
                Subtotal       = i.Subtotal,
                AppliedRuleName = i.AppliedRuleName          // UPDATED
            }).ToList() ?? new List<SaleItemDto>(),
            // UPDATED â€” split-payment breakdown
            Payments = s.Payments?.Select(p => new SalePaymentDto
            {
                Id       = p.Id,
                Method   = p.Method,
                Amount   = p.Amount,
                Tendered = p.Tendered,
                Change   = p.Method == "cash" && p.Tendered.HasValue
                           ? Math.Max(0, p.Tendered.Value - p.Amount)
                           : 0
            }).ToList() ?? new List<SalePaymentDto>()
        };
    }
}
