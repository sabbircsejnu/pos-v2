using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Infrastructure.Grns;

public sealed class GrnCreationStore(
    RetailPOSDbContext dbContext,
    ILogger<GrnCreationStore> logger) : IGrnCreationStore
{
    public async Task<GrnCreationPurchaseOrderReadModel?> FindPurchaseOrderAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Warehouse)
            .Include(order => order.Outlet)
            .Include(order => order.Items)
            .Where(order => order.Id == purchaseOrderId)
            .WhereBusiness(businessId);

        var order = await query.SingleOrDefaultAsync(cancellationToken);
        return order is null
            ? null
            : new GrnCreationPurchaseOrderReadModel(
                order.Id,
                order.LocationBusinessId,
                order.SupplierId,
                order.LocationId,
                order.LocationType,
                order.Status,
                order.Items.Select(item => new GrnCreationPurchaseOrderItemReadModel(
                    item.Id,
                    item.Quantity,
                    item.UnitPrice,
                    item.Discount,
                    item.Tax,
                    item.TaxInclusive,
                    item.VariantId,
                    item.ReadUomSnapshot()))
                    .ToArray());
    }

    public async Task<IReadOnlyDictionary<long, decimal>> GetCompletedReceivedQuantitiesAsync(
        long purchaseOrderId,
        CancellationToken cancellationToken) =>
        await dbContext.GrnItems
            .AsNoTracking()
            .Where(item => item.Grn.PoId == purchaseOrderId && item.Grn.Status == "completed")
            .GroupBy(item => item.PoItemId)
            .ToDictionaryAsync(
                group => group.Key,
                group => group.Sum(item => item.ReceivedQty),
                cancellationToken);

    public async Task<GrnReadModel> CreateAsync(
        GrnCreationRecord record,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var purchaseOrderExists = dbContext.PurchaseOrders
            .Where(order => order.Id == record.PurchaseOrderId)
            .WhereBusiness(businessId);
        if (!await purchaseOrderExists.AnyAsync(cancellationToken))
            throw new KeyNotFoundException(
                $"Purchase order with ID {record.PurchaseOrderId} not found");

        var grn = new Grn
        {
            PoId = record.PurchaseOrderId,
            ReceivedDate = record.ReceivedDate,
            Status = record.Status,
            Notes = record.Notes,
            SupplierInvoiceNumber = record.SupplierInvoiceNumber,
            SupplierInvoiceDate = record.SupplierInvoiceDate,
            CreatedBy = record.CreatedBy,
            CreatedAt = DateTime.UtcNow,
            Items = record.Items.Select(item => new GrnItem
            {
                PoItemId = item.PurchaseOrderItemId,
                ReceivedQty = item.ReceivedQuantity,
                UnitCost = item.UnitCost,
                TaxableAmount = item.TaxableAmount,
                VatRate = item.VatRate,
                VatAmount = item.VatAmount,
                TaxCode = item.TaxCode,
                TaxInclusive = item.TaxInclusive,
                Notes = item.Notes,
                DamagedQuantity = item.DamagedQuantity,
                RejectedQuantity = item.RejectedQuantity,
                AcceptedSealedPacks = item.AcceptedSealedPacks,
                AcceptedLooseQuantity = item.AcceptedLooseQuantity
            }).ToList()
        };
        dbContext.Grns.Add(grn);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "GRN {GrnId} created for PO {PoId} by user {UserId}",
            grn.Id,
            record.PurchaseOrderId,
            record.CreatedBy);

        var created = await dbContext.Grns
            .AsNoTracking()
            .Include(candidate => candidate.PurchaseOrder)
                .ThenInclude(order => order.Supplier)
            .Include(candidate => candidate.PurchaseOrder)
                .ThenInclude(order => order.Warehouse)
            .Include(candidate => candidate.PurchaseOrder)
                .ThenInclude(order => order.Outlet)
            .Include(candidate => candidate.PurchaseOrder)
                .ThenInclude(order => order.Creator)
            .Include(candidate => candidate.Creator)
            .Include(candidate => candidate.Items)
                .ThenInclude(item => item.PurchaseOrderItem)
                    .ThenInclude(item => item.Variant)
                        .ThenInclude(variant => variant.Product)
            .SingleAsync(candidate => candidate.Id == grn.Id, cancellationToken);
        return GrnReadModelMapper.Map(created);
    }
}
