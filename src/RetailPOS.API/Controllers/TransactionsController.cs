using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Transaction;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(ITransactionService transactionService, ILogger<TransactionsController> logger)
    {
        _transactionService = transactionService;
        _logger = logger;
    }

    /// <summary>List transactions with optional filters</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<TransactionListDto>>> GetAll(
        [FromQuery] long? accountId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? type)
    {
        var searchDto = new TransactionSearchDto
        {
            AccountId = accountId,
            StartDate = startDate,
            EndDate = endDate,
            Type = type
        };
        var result = await _transactionService.SearchAsync(searchDto);
        return Ok(ApiResponse<TransactionListDto>.SuccessResponse(result));
    }

    /// <summary>Paginated transaction search</summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<TransactionListDto>>> Search([FromBody] TransactionSearchDto searchDto)
    {
        var result = await _transactionService.SearchAsync(searchDto);
        return Ok(ApiResponse<TransactionListDto>.SuccessResponse(result));
    }

    /// <summary>Get transaction by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<TransactionDto>>> GetById(long id)
    {
        var transaction = await _transactionService.GetByIdAsync(id);
        return Ok(ApiResponse<TransactionDto>.SuccessResponse(transaction));
    }

    /// <summary>Create a new transaction and update account balance</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<TransactionDto>>> Create([FromBody] CreateTransactionDto dto)
    {
        var transaction = await _transactionService.CreateAsync(dto);
        return CreatedAtAction(
            nameof(GetById),
            new { id = transaction.Id },
            ApiResponse<TransactionDto>.SuccessResponse(transaction, "Transaction created successfully"));
    }

    /// <summary>Get all transactions for a specific account (ledger view)</summary>
    [HttpGet("ledger")]
    public async Task<ActionResult<ApiResponse<IEnumerable<TransactionDto>>>> GetLedger([FromQuery] long accountId)
    {
        var transactions = await _transactionService.GetLedgerAsync(accountId);
        return Ok(ApiResponse<IEnumerable<TransactionDto>>.SuccessResponse(transactions));
    }
}
