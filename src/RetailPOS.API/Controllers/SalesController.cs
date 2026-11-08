using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using System.Security.Claims;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.Commands;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Queries;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Application.Sales.Commands;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize(Policy = "sales.view")]
public class SalesController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccess;
    private readonly IFeatureEntitlementService _featureEntitlement;
    private readonly ILogger<SalesController> _logger;
    private readonly ISender _sender;
    private readonly ICurrentCompanyTimeZone _companyTimeZone;
    private readonly ISaleReadScopeProvider _saleReadScope;

    public SalesController(
        IUserOutletAccessService outletAccess,
        IFeatureEntitlementService featureEntitlement,
        ILogger<SalesController> logger,
        ISender sender,
        ICurrentCompanyTimeZone companyTimeZone,
        ISaleReadScopeProvider saleReadScope)
    {
        _outletAccess = outletAccess;
        _featureEntitlement = featureEntitlement;
        _logger = logger;
        _sender = sender;
        _companyTimeZone = companyTimeZone;
        _saleReadScope = saleReadScope;
    }

    /// <summary>List sales with filters (query string)</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SaleListDto>>> GetAll(
        [FromQuery] SaleSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var result = await SearchSalesAsync(searchDto, cancellationToken);
        return Ok(ApiResponse<SaleListDto>.SuccessResponse(ToListDto(result)));
    }

    /// <summary>Get a sale by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var sale = await _sender.Send(new GetSaleByIdQuery(id), cancellationToken);
        await _saleReadScope.GetAsync(sale.OutletId, cancellationToken);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(ToDto(sale)));
    }

    /// <summary>
    /// Cost is left out of the response, not just hidden on screen, for a caller without <c>products.view_cost</c>
    /// (null, never a misleading 0). Prices and quantities stay visible to everyone who may read the sale.
    /// </summary>
    private SaleDto ToDto(SaleReadModel sale)
    {
        var dto = MapSale(sale);
        if (!User.CanViewCost())
            foreach (var item in dto.Items)
                item.HistoricalUnitCost = null;
        return dto;
    }

    private static SaleDto MapSale(SaleReadModel sale) => new()
    {
        Id = sale.Id,
        SaleNumber = sale.SaleNumber,
        OutletId = sale.OutletId,
        OutletName = sale.OutletName ?? string.Empty,
        TerminalId = sale.TerminalId,
        TerminalName = sale.TerminalName,
        CashSessionId = sale.CashSessionId,
        BusinessDate = sale.BusinessDate,
        SaleDateTime = sale.SaleDateTime,
        CustomerId = sale.CustomerId,
        CustomerName = sale.CustomerName,
        CashierId = sale.CashierId,
        CashierName = sale.CashierName ?? string.Empty,
        SalespersonUserId = sale.SalespersonUserId,
        SalespersonName = sale.SalespersonName,
        SaleDate = sale.SaleDate,
        TotalAmount = sale.TotalAmount,
        Discount = sale.Discount,
        Tax = sale.Tax,
        NetTotal = sale.NetTotal,
        PaymentMethod = sale.PaymentMethod,
        Status = sale.Status,
        CreatedAt = sale.CreatedAt,
        Items = sale.Items.Select(item => new SaleItemDto
        {
            Id = item.Id,
            VariantId = item.VariantId,
            ProductName = item.ProductName ?? string.Empty,
            VariantSku = item.VariantSku,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            HistoricalUnitCost = item.HistoricalUnitCost,
            DiscountAmount = item.DiscountAmount,
            Subtotal = item.Subtotal,
            AppliedRuleName = item.AppliedRuleName,
            UnitCode = item.UnitCode,
            UnitLabel = item.UnitLabel,
            EnteredQuantity = item.EnteredQuantity,
            EnteredUnitPrice = item.EnteredUnitPrice,
            BaseUnitCode = item.BaseUnitCode,
            PackState = item.PackState,
            ReturnedQuantity = item.ReturnedQuantity,
            RemainingReturnableQuantity = item.RemainingReturnableQuantity
        }).ToList(),
        Payments = sale.Payments.Select(payment => new SalePaymentDto
        {
            Id = payment.Id,
            Method = payment.Method,
            Amount = payment.Amount,
            Tendered = payment.Tendered,
            Change = payment.Change
        }).ToList(),
        Refunds = sale.Refunds.Select(refund => new SaleRefundDto
        {
            Id = refund.Id,
            RefundNumber = refund.RefundNumber,
            Sequence = refund.Sequence,
            CreatedAtUtc = refund.CreatedAtUtc,
            Reason = refund.Reason,
            ActorUserId = refund.ActorUserId,
            ActorName = refund.ActorName,
            NetAmount = refund.NetAmount,
            VatAmount = refund.VatAmount,
            TotalAmount = refund.TotalAmount,
            CashAmount = refund.CashAmount,
            Lines = refund.Lines.Select(line => new SaleRefundLineDto
            {
                SaleItemId = line.SaleItemId,
                Quantity = line.Quantity,
                Condition = line.Condition,
                RestockedQuantity = line.RestockedQuantity,
                PackagingReusable = line.PackagingReusable,
                NetAmount = line.NetAmount,
                VatAmount = line.VatAmount
            }).ToList(),
            Payments = refund.Payments.Select(payment => new SaleRefundPaymentDto
            {
                Method = payment.Method,
                Amount = payment.Amount
            }).ToList()
        }).ToList()
    };

    private static ReceiptDocumentDto MapReceiptDocument(SaleReceiptDocumentReadModel document) => new()
    {
        OutputMode = document.OutputMode,
        Header = new ReceiptHeaderDto
        {
            ShowLogo = document.Header.ShowLogo,
            LogoBase64 = document.Header.LogoBase64,
            BusinessName = document.Header.BusinessName,
            OutletName = document.Header.OutletName,
            OutletAddress = document.Header.OutletAddress,
            Phone = document.Header.Phone,
            BinOrVatNo = document.Header.BinOrVatNo,
            HeaderText = document.Header.HeaderText,
            ConcernOf = document.Header.ConcernOf,
            BranchName = document.Header.BranchName,
            ShowroomHotline = document.Header.ShowroomHotline,
            CentralHotline = document.Header.CentralHotline,
            Website = document.Header.Website,
            TaxIdentityLine = document.Header.TaxIdentityLine
        },
        SaleInfo = new ReceiptSaleInfoDto
        {
            InvoiceNumber = document.SaleInfo.InvoiceNumber,
            SalesDateTime = document.SaleInfo.SalesDateTime,
            Cashier = document.SaleInfo.Cashier,
            Terminal = document.SaleInfo.Terminal,
            CustomerName = document.SaleInfo.CustomerName,
            Salesperson = document.SaleInfo.Salesperson,
            BusinessDate = document.SaleInfo.BusinessDate,
            CustomerMobile = document.SaleInfo.CustomerMobile,
            InvoiceDateText = document.SaleInfo.InvoiceDateText
        },
        Items = document.Items.Select(item => new ReceiptLineItemDto
        {
            ProductName = item.ProductName,
            VariantLabel = item.VariantLabel,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            Discount = item.Discount,
            LineTotal = item.LineTotal,
            VatRate = item.VatRate,
            VatAmount = item.VatAmount,
            PrintName = item.PrintName,
            DiscountPercent = item.DiscountPercent
        }).ToList(),
        Payments = document.Payments.Select(payment => new ReceiptPaymentDto
        {
            Method = payment.Method,
            Amount = payment.Amount,
            Tendered = payment.Tendered,
            Change = payment.Change
        }).ToList(),
        TaxInclusive = document.TaxInclusive,
        AmountInWords = document.AmountInWords,
        PaymentLines = document.PaymentLines
            .Select(line => new ReceiptPaymentLineDto { Label = line.Label, Amount = line.Amount })
            .ToList(),
        Summary = new ReceiptSummaryDto
        {
            ItemCount = document.Summary.ItemCount,
            Quantity = document.Summary.Quantity,
            Subtotal = document.Summary.Subtotal,
            Discount = document.Summary.Discount,
            Tax = document.Summary.Tax,
            RoundOff = document.Summary.RoundOff,
            GrandTotal = document.Summary.GrandTotal,
            PaymentMethod = document.Summary.PaymentMethod,
            PaidAmount = document.Summary.PaidAmount,
            Change = document.Summary.Change
        },
        Footer = new ReceiptFooterDto
        {
            ThankYouMessage = document.Footer.ThankYouMessage,
            ReturnPolicy = document.Footer.ReturnPolicy,
            FooterText = document.Footer.FooterText,
            ShowBarcode = document.Footer.ShowBarcode,
            ShowQr = document.Footer.ShowQr,
            CodeValue = document.Footer.CodeValue,
            GeneratedAt = document.Footer.GeneratedAt,
            VatLawNotification = document.Footer.VatLawNotification,
            PoweredByText = document.Footer.PoweredByText
        }
    };

    private async Task<SaleSearchReadModel> SearchSalesAsync(
        SaleSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var criteria = new SaleSearchCriteria(
            searchDto.OutletId,
            searchDto.CashierId,
            searchDto.CustomerId,
            searchDto.StartDate,
            searchDto.EndDate,
            searchDto.Status,
            searchDto.PageNumber,
            searchDto.PageSize);
        return await _sender.Send(new SearchSalesQuery(criteria), cancellationToken);
    }

    private SaleListDto ToListDto(SaleSearchReadModel result) => new()
    {
        Sales = result.Sales.Select(sale => ToDto(sale)).ToList(),
        TotalCount = result.TotalCount,
        PageNumber = result.PageNumber,
        PageSize = result.PageSize
    };

    /// <summary>Get receipt data for a sale</summary>
    [HttpGet("{id}/receipt")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> GetReceipt(
        long id,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetSaleByIdQuery(id), cancellationToken);
        await _saleReadScope.GetAsync(existing.OutletId, cancellationToken);

        var sale = await _sender.Send(
            new PrintSaleReceiptCommand(id, TryGetCurrentUserId()),
            cancellationToken);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(ToDto(sale)));
    }

    /// <summary>Get canonical receipt document payload for rendering.</summary>
    [HttpGet("{id}/receipt-document")]
    public async Task<ActionResult<ApiResponse<ReceiptDocumentDto>>> GetReceiptDocument(
        long id,
        [FromQuery] string? mode,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetSaleByIdQuery(id), cancellationToken);
        await _saleReadScope.GetAsync(existing.OutletId, cancellationToken);

        var document = await _sender.Send(
            new GenerateSaleReceiptDocumentCommand(id, mode, TryGetCurrentUserId()),
            cancellationToken);
        return Ok(ApiResponse<ReceiptDocumentDto>.SuccessResponse(MapReceiptDocument(document)));
    }

    /// <summary>Generate receipt PDF for download using canonical receipt document payload.</summary>
    [HttpGet("{id}/receipt-pdf")]
    public async Task<IActionResult> GetReceiptPdf(
        long id,
        [FromQuery] string? mode,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetSaleByIdQuery(id), cancellationToken);
        await _saleReadScope.GetAsync(existing.OutletId, cancellationToken);

        var pdf = await _sender.Send(
            new GenerateSaleReceiptPdfCommand(id, mode, TryGetCurrentUserId()),
            cancellationToken);
        var fileName = $"receipt-{existing.SaleNumber}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        return File(pdf.Content, "application/pdf", fileName);
    }

    /// <summary>Register client-side receipt print attempt outcome (printed/reprint/failure/pdf fallback).</summary>
    [HttpPost("{id}/receipt-print-attempt")]
    public async Task<ActionResult<ApiResponse<object>>> RegisterReceiptPrintAttempt(
        long id,
        [FromBody] ReceiptPrintAttemptDto dto,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetSaleByIdQuery(id), cancellationToken);
        await _saleReadScope.GetAsync(existing.OutletId, cancellationToken);

        await _sender.Send(
            new RegisterSaleReceiptAttemptCommand(
                id,
                dto.Action,
                dto.OutputMode,
                dto.Message,
                TryGetCurrentUserId()),
            cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse("Receipt attempt recorded."));
    }

    /// <summary>Register and return receipt payload for sale reprint</summary>
    [HttpPost("{id}/reprint")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Reprint(
        long id,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetSaleByIdQuery(id), cancellationToken);
        await _saleReadScope.GetAsync(existing.OutletId, cancellationToken);

        var sale = await _sender.Send(
            new ReprintSaleReceiptCommand(id, TryGetCurrentUserId()),
            cancellationToken);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(
            ToDto(sale),
            "Receipt reprint payload generated"));
    }

    /// <summary>Get today's sales summary</summary>
    [HttpGet("today/summary")]
    public async Task<ActionResult<ApiResponse<SaleSummaryDto>>> TodaysSummary(
        [FromQuery] long? outletId = null,
        CancellationToken cancellationToken = default)
    {
        var summary = await _sender.Send(
            new GetSaleTodaySummaryQuery(outletId),
            cancellationToken);
        return Ok(ApiResponse<SaleSummaryDto>.SuccessResponse(ToDto(summary)));
    }

    private static SaleSummaryDto ToDto(SaleTodaySummaryReadModel summary) => new()
    {
        TotalSales = summary.TotalSales,
        TotalAmount = summary.TotalAmount,
        TotalDiscount = summary.TotalDiscount,
        TotalTax = summary.TotalTax,
        PaymentBreakdown = summary.PaymentBreakdown.ToDictionary(
            item => item.Key,
            item => item.Value)
    };

    /// <summary>Search sales with pagination (request body)</summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<SaleListDto>>> Search(
        [FromBody] SaleSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var result = await SearchSalesAsync(searchDto, cancellationToken);
        return Ok(ApiResponse<SaleListDto>.SuccessResponse(ToListDto(result)));
    }

    /// <summary>Create a new sale</summary>
    [HttpPost]
    [Authorize(Policy = "sales.create")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Create(
        [FromBody] CreateSaleDto dto,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var canBackdate = User.HasPermission("sales.backdate");

        if (dto.SalesDate.HasValue)
        {
            var companyZone = await _companyTimeZone.GetTimeZoneIdAsync(HttpContext.RequestAborted);
            var companyToday = CompanyDates.CalendarDateOf(now, companyZone);
            var requestedDate = CompanyDates.CalendarDateOf(dto.SalesDate.Value, companyZone);
            if (requestedDate > companyToday
                || (dto.SalesDate.Value.Kind != DateTimeKind.Unspecified && dto.SalesDate.Value.ToUniversalTime() > now))
            {
                return BadRequest(ApiResponse<SaleDto>.ErrorResponse("Sales Order Date cannot be in the future."));
            }

            if (!canBackdate && requestedDate < companyToday)
            {
                return BadRequest(ApiResponse<SaleDto>.ErrorResponse("You are not allowed to create backdated POS sales."));
            }

            // A picked date means that day in the Company zone, not midnight UTC.
            dto.SalesDate = CompanyDates.StartUtc(dto.SalesDate.Value, companyZone);
        }


        if (!canBackdate)
        {
            dto.SalesDate = now;
        }
        else if (!dto.SalesDate.HasValue)
        {
            dto.SalesDate = now;
        }

        // Server-side outlet enforcement: never trust the client-supplied OutletId.
        // Non-BusinessOwner is pinned to their default outlet; BusinessOwner can use
        // any outlet in their authorized set.
        dto.OutletId = await _outletAccess.EnforceWriteOutletAsync(dto.OutletId);

        // The cashier is always the authenticated principal; a client-supplied identity is only accepted when it matches.
        var cashierUserId = TryGetCurrentUserId();
        if (!cashierUserId.HasValue || cashierUserId.Value <= 0)
        {
            return Unauthorized(ApiResponse<SaleDto>.ErrorResponse("User identity not found"));
        }

        if (dto.CashierId.HasValue && dto.CashierId.Value != cashierUserId.Value)
        {
            _logger.LogWarning(
                "Rejected sale request for outlet {OutletId}: submitted cashier {SubmittedCashierId} does not match authenticated user {UserId}",
                dto.OutletId,
                dto.CashierId.Value,
                cashierUserId.Value);
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<SaleDto>.ErrorResponse("Cashier identity must match the authenticated user."));
        }

        var sale = await _sender.Send(
            new CreateSaleCommand(
                dto.OutletId,
                dto.TerminalId,
                dto.CustomerId,
                dto.Items.Select(item => new CreateSaleLine(
                    item.VariantId,
                    item.Quantity,
                    item.UnitPrice,
                    item.DiscountAmount,
                    item.AppliedRuleId,
                    item.AppliedRuleName,
                    item.SellingOptionId,
                    item.ExpectedConfigVersionId,
                    item.FulfilmentState,
                    item.IncludePackaging,
                    item.ExpectedLineGross)).ToArray(),
                dto.Discount,
                dto.Tax,
                dto.PaymentMethod,
                dto.Payments.Select(payment => new CreateSalePayment(
                    payment.Method,
                    payment.Amount,
                    payment.Tendered)).ToArray(),
                cashierUserId.Value,
                dto.OriginalSaleId,
                dto.SalesDate,
                dto.IdempotencyKey,
                dto.CashTendered,
                User.HasPermission("sales.discount.apply"),
                User.HasPermission("sales.discount.override_limit"),
                dto.SalespersonUserId),
            cancellationToken);

        var saleDto = ToDto(sale);
        await TryRecordBackdatedSaleAuditAsync(saleDto, dto.SalesDate);

        return CreatedAtAction(
            nameof(GetById),
            new { id = saleDto.Id },
            ApiResponse<SaleDto>.SuccessResponse(saleDto, "Sale created successfully"));
    }

    private async Task TryRecordBackdatedSaleAuditAsync(SaleDto sale, DateTime? requestedSalesDate)
    {
        try
        {
            var createdAt = sale.CreatedAt;
            var salesDate = sale.SaleDate;

            var companyZone = await _companyTimeZone.GetTimeZoneIdAsync(CancellationToken.None);
            if (CompanyDates.DateFor(salesDate, companyZone) >= CompanyDates.DateFor(createdAt, companyZone))

            {
                return;
            }

            var userId = TryGetCurrentUserId();

            await _sender.Send(new RecordAuditEventCommand(new AuditEventWriteRequest(
                AuditActionType.Create,
                AuditModule.Sales,
                $"Backdated POS sale created: {sale.SaleNumber}",
                "Sale",
                sale.Id.ToString(),
                Metadata: new Dictionary<string, object?>
                {
                    ["userId"] = userId,
                    ["salesDate"] = salesDate,
                    ["createdAt"] = createdAt,
                    ["outletId"] = sale.OutletId,
                    ["terminalId"] = sale.TerminalId,
                    ["saleNumber"] = sale.SaleNumber,
                    ["requestedSalesDate"] = requestedSalesDate
                })), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record backdated sale audit for sale {SaleId}", sale.Id);
        }
    }

    /// <summary>Void a completed sale</summary>
    [HttpPost("{id}/void")]
    [Authorize(Policy = "sales.void")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Void(
        long id,
        [FromBody] VoidSaleDto dto,
        CancellationToken cancellationToken)
    {
        var actorUserId = TryGetCurrentUserId();
        if (!actorUserId.HasValue || actorUserId.Value <= 0)
        {
            return Unauthorized(ApiResponse<SaleDto>.ErrorResponse("User identity not found"));
        }

        var existing = await _sender.Send(new GetSaleByIdQuery(id), cancellationToken);
        await EnsureSaleOutletAuthorizedAsync(ToDto(existing));

        var sale = await _sender.Send(
            new VoidSaleCommand(id, dto.Reason, actorUserId.Value, dto.TerminalId),
            cancellationToken);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(ToDto(sale), "Sale voided successfully"));
    }

    /// <summary>Refund a sale</summary>
    [HttpPost("{id}/refund")]
    [Authorize(Policy = "sales.refund")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Refund(
        long id,
        [FromBody] RefundSaleDto dto,
        CancellationToken cancellationToken)
    {
        var actorUserId = TryGetCurrentUserId();
        if (!actorUserId.HasValue || actorUserId.Value <= 0)
        {
            return Unauthorized(ApiResponse<SaleDto>.ErrorResponse("User identity not found"));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _featureEntitlement.EnsureFeatureEnabledAsync(
            "sales.refund",
            cancellationToken: cancellationToken);

        var existing = await _sender.Send(new GetSaleByIdQuery(id), cancellationToken);
        await EnsureSaleOutletAuthorizedAsync(ToDto(existing));

        var sale = await _sender.Send(
            new RefundSaleCommand(
                id,
                dto.Items?.Select(item => new RefundSaleLine(
                    item.VariantId,
                    item.Quantity,
                    item.SaleItemId,
                    item.Condition,
                    item.PackagingReusable)).ToArray() ?? [],
                dto.Reason,
                actorUserId.Value,
                dto.TerminalId,
                dto.CashRefundAmount,
                dto.IdempotencyKey),
            cancellationToken);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(ToDto(sale), "Sale refunded successfully"));
    }

    private async Task EnsureSaleOutletAuthorizedAsync(SaleDto sale)
    {
        // Void and Refund change money and stock, so they follow the outlet policy even for a Business Owner
        // whose read access is Business-wide. Throws UnauthorizedAccessException outside it.
        await _outletAccess.ResolveAndAuthorizeLocationAsync(sale.OutletId, "outlet");
    }

    private long? TryGetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return long.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
