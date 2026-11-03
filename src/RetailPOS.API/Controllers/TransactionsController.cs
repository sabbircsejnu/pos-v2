using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Transaction;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Transactions.Queries;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "transactions.view")]
public class TransactionsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(ISender sender, ILogger<TransactionsController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>List transactions with optional filters</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<TransactionListDto>>> GetAll(
        [FromQuery] long? accountId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? type,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new SearchTransactionsQuery(accountId, startDate, endDate, type, 1, 20),
            cancellationToken);
        return Ok(ApiResponse<TransactionListDto>.SuccessResponse(MapSearchResult(result)));
    }

    /// <summary>Paginated transaction search</summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<TransactionListDto>>> Search(
        [FromBody] TransactionSearchDto searchDto,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new SearchTransactionsQuery(
                searchDto.AccountId,
                searchDto.StartDate,
                searchDto.EndDate,
                searchDto.Type,
                searchDto.PageNumber,
                searchDto.PageSize),
            cancellationToken);
        return Ok(ApiResponse<TransactionListDto>.SuccessResponse(MapSearchResult(result)));
    }

    /// <summary>Get transaction by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<TransactionDto>>> GetById(
        long id,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _sender.Send(
            new GetTransactionByIdQuery(id),
            cancellationToken);
        return Ok(ApiResponse<TransactionDto>.SuccessResponse(MapTransaction(transaction)));
    }

    /// <summary>Create a new transaction and update account balance</summary>
    [HttpPost]
    [Authorize(Policy = "transactions.create")]
    public async Task<ActionResult<ApiResponse<TransactionDto>>> Create([FromBody] CreateTransactionDto dto)
    {
        _logger.LogWarning("Blocked legacy Transaction create request. Journal Engine is authoritative.");
        return Conflict(ApiResponse<TransactionDto>.ErrorResponse("Legacy Transaction writes are disabled. Use Accounting Journals and the Journal Engine."));
    }

    /// <summary>Get all transactions for a specific account (ledger view)</summary>
    [HttpGet("ledger")]
    public async Task<ActionResult<ApiResponse<IEnumerable<TransactionDto>>>> GetLedger(
        [FromQuery] long accountId,
        CancellationToken cancellationToken = default)
    {
        var transactions = await _sender.Send(
            new GetAccountTransactionLedgerQuery(accountId),
            cancellationToken);
        return Ok(ApiResponse<IEnumerable<TransactionDto>>.SuccessResponse(
            transactions.Select(MapTransaction)));
    }

    private static TransactionListDto MapSearchResult(
        RetailPOS.Application.Transactions.TransactionListReadModel result) => new()
    {
        Transactions = result.Transactions.Select(MapTransaction).ToList(),
        TotalCount = result.TotalCount,
        PageNumber = result.PageNumber,
        PageSize = result.PageSize
    };

    private static TransactionDto MapTransaction(
        RetailPOS.Application.Transactions.TransactionReadModel transaction) => new()
    {
        Id = transaction.Id,
        AccountId = transaction.AccountId,
        AccountName = transaction.AccountName,
        Amount = transaction.Amount,
        Type = transaction.Type,
        Description = transaction.Description,
        TransactionDate = transaction.TransactionDate,
        ReferenceType = transaction.ReferenceType,
        ReferenceId = transaction.ReferenceId
    };
}
