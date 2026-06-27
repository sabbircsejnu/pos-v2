using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.Documents.Pdf;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentPdfDispatcher _pdfDispatcher;

    public DocumentsController(IDocumentPdfDispatcher pdfDispatcher)
    {
        _pdfDispatcher = pdfDispatcher;
    }

    [HttpGet("{documentType}/{documentId:long}/pdf")]
    public async Task<IActionResult> DownloadPdf(string documentType, long documentId, CancellationToken cancellationToken)
    {
        if (!CanViewDocumentType(documentType))
        {
            return Forbid();
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