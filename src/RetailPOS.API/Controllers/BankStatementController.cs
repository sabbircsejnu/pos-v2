using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.BankStatements.Commands;
using RetailPOS.Application.BankStatements.Queries;
using RetailPOS.Application.BankStatements.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/bank-statements")]
[Authorize]
public sealed class BankStatementController : ControllerBase
{
    private readonly ISender _sender;

    public BankStatementController(ISender sender) => _sender = sender;

    [HttpPost("imports")]
    [Authorize(Policy = "accounting.statement_import.create")]
    public async Task<ActionResult<ApiResponse<BankStatementImportDto>>> Import(
        [FromForm] BankStatementImportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File == null) return BadRequest(ApiResponse<BankStatementImportDto>.ErrorResponse("CSV file is required."));
        var userId = long.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : throw new UnauthorizedAccessException("User identity not found.");
        await using var stream = request.File.OpenReadStream();
        var result = await _sender.Send(
            new ImportBankStatementCsvCommand(
                userId,
                request.BankAccountId,
                request.File.FileName,
                stream),
            cancellationToken);
        return Ok(ApiResponse<BankStatementImportDto>.SuccessResponse(
            Map(result),
            "Statement imported successfully"));
    }

    [HttpGet("imports")]
    [Authorize(Policy = "accounting.statement_import.view")]
    public async Task<ActionResult<ApiResponse<List<BankStatementImportDto>>>> ListImports(
        [FromQuery] long? bankAccountId,
        CancellationToken cancellationToken)
    {
        var imports = await _sender.Send(
            new GetBankStatementImportsQuery(bankAccountId),
            cancellationToken);
        return Ok(ApiResponse<List<BankStatementImportDto>>.SuccessResponse(
            imports.Select(Map).ToList()));
    }

    [HttpGet("imports/{id:long}")]
    [Authorize(Policy = "accounting.statement_import.view")]
    public async Task<ActionResult<ApiResponse<BankStatementImportDto>>> GetImport(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetBankStatementImportQuery(id),
            cancellationToken);
        return result == null
            ? NotFound()
            : Ok(ApiResponse<BankStatementImportDto>.SuccessResponse(Map(result)));
    }

    [HttpGet("lines")]
    [Authorize(Policy = "accounting.statement_import.view")]
    public async Task<ActionResult<ApiResponse<List<BankStatementLineDto>>>> ListLines(
        [FromQuery] long? importId,
        [FromQuery] long? bankAccountId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        CancellationToken cancellationToken)
    {
        var lines = await _sender.Send(
            new GetBankStatementLinesQuery(importId, bankAccountId, dateFrom, dateTo),
            cancellationToken);
        return Ok(ApiResponse<List<BankStatementLineDto>>.SuccessResponse(
            lines.Select(Map).ToList()));
    }

    private static BankStatementImportDto Map(BankStatementImportReadModel result) => new()
    {
        Id = result.Id,
        BusinessId = result.BusinessId,
        BankAccountId = result.BankAccountId,
        SourceFileName = result.SourceFileName,
        SourceFileChecksum = result.SourceFileChecksum,
        SourceFormat = result.SourceFormat,
        ImportedAt = result.ImportedAt,
        StatementPeriodStart = result.StatementPeriodStart,
        StatementPeriodEnd = result.StatementPeriodEnd,
        OpeningBalance = result.OpeningBalance,
        ClosingBalance = result.ClosingBalance,
        ImportedRowCount = result.ImportedRowCount,
        AcceptedRowCount = result.AcceptedRowCount,
        RejectedRowCount = result.RejectedRowCount,
        Status = result.Status,
        ErrorSummary = result.ErrorSummary,
        CreatedBy = result.CreatedBy
    };

    private static BankStatementLineDto Map(BankStatementLineReadModel result) => new()
    {
        Id = result.Id,
        BusinessId = result.BusinessId,
        BankAccountId = result.BankAccountId,
        BankStatementImportId = result.BankStatementImportId,
        SourceRowNumber = result.SourceRowNumber,
        TransactionDate = result.TransactionDate,
        ValueDate = result.ValueDate,
        ExternalTransactionReference = result.ExternalTransactionReference,
        Description = result.Description,
        DebitAmount = result.DebitAmount,
        CreditAmount = result.CreditAmount,
        SignedAmount = result.SignedAmount,
        RunningBalance = result.RunningBalance,
        Currency = result.Currency,
        RawReferenceText = result.RawReferenceText
    };
}
