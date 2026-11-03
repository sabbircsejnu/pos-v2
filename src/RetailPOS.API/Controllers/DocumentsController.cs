using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.Documents.Pdf;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Queries;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentPdfDispatcher _pdfDispatcher;
    private readonly IUserOutletAccessService _outletAccess;
    private readonly ISender _sender;

    public DocumentsController(
        IDocumentPdfDispatcher pdfDispatcher,
        IUserOutletAccessService outletAccess,
        ISender sender)
    {
        _pdfDispatcher = pdfDispatcher;
        _outletAccess = outletAccess;
        _sender = sender;
    }

    [HttpGet("{documentType}/{documentId:long}/pdf")]
    public async Task<IActionResult> DownloadPdf(string documentType, long documentId, CancellationToken cancellationToken)
    {
        if (!CanViewDocumentType(documentType))
        {
            return Forbid();
        }

        if (IsStockTransferDocumentType(documentType))
        {
            var visibilityError = await GetStockTransferVisibilityErrorAsync(documentId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(visibilityError))
            {
                if (visibilityError.Equals("Your default inventory location is not configured.", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = visibilityError });
                }

                return StatusCode(StatusCodes.Status403Forbidden, new { message = visibilityError });
            }
        }

        try
        {
            var result = await _pdfDispatcher.GenerateAsync(documentType, documentId, User.CanViewCost(), cancellationToken);
            return File(result.Content, "application/pdf", result.FileName);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static bool IsStockTransferDocumentType(string documentType)
    {
        var normalized = documentType.Trim().ToLowerInvariant();
        return normalized is "stock-transfer" or "stock_transfer";
    }

    private async Task<string?> GetStockTransferVisibilityErrorAsync(
        long transferId,
        CancellationToken cancellationToken)
    {
        var canViewAllLocations = User.HasPermission("stock_transfers.view_all_locations");
        string? defaultLocationType = null;
        long? defaultLocationId = null;

        if (!canViewAllLocations)
        {
            var auth = await _outletAccess.GetAuthorizedOutletsAsync();
            if (!auth.DefaultLocationId.HasValue || string.IsNullOrWhiteSpace(auth.DefaultLocationType))
                return "Your default inventory location is not configured.";

            defaultLocationType = auth.DefaultLocationType;
            defaultLocationId = auth.DefaultLocationId;
        }

        var transfer = await _sender.Send(new GetStockTransferByIdQuery(transferId), cancellationToken);
        if (transfer is null)
            throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");

        if (canViewAllLocations)
            return null;

        var matchesFrom = string.Equals(transfer.FromLocationType, defaultLocationType, StringComparison.OrdinalIgnoreCase)
            && transfer.FromLocationId == defaultLocationId;
        var matchesTo = string.Equals(transfer.ToLocationType, defaultLocationType, StringComparison.OrdinalIgnoreCase)
            && transfer.ToLocationId == defaultLocationId;

        return matchesFrom || matchesTo
            ? null
            : "You are not authorized to view this stock transfer.";
    }

    private bool CanViewDocumentType(string documentType)
    {
        return documentType.Trim().ToLowerInvariant() switch
        {
            "purchase-order" or "purchase_order" or "po" => User.HasPermission("purchases.view"),
            "sales-invoice" or "sales_invoice" or "invoice" => User.HasPermission("sales.view"),
            "purchase-invoice" or "purchase_invoice" => User.HasPermission("accounts.view") || User.HasPermission("purchases.view"),
            "sales-return" or "sales_return" => User.HasPermission("sales.refund") || User.HasPermission("sales.view"),
            "purchase-return" or "purchase_return" => User.HasPermission("purchases.edit") || User.HasPermission("purchases.view"),
            "grn" => User.HasPermission("grn.view"),
            "stock-transfer" or "stock_transfer" => User.HasPermission("stock_transfers.view") || User.HasPermission("inventory.view"),
            "quotation" or "estimate" => User.HasPermission("sales.view"),
            _ => false
        };
    }
}