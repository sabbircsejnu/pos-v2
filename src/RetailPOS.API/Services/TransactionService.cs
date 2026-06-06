using RetailPOS.API.DTOs.Transaction;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Transaction business logic
/// </summary>
public class TransactionService : ITransactionService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantAccessService _tenantAccess;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        ITenantAccessService tenantAccess,
        ILogger<TransactionService> logger)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _tenantAccess = tenantAccess;
        _logger = logger;
    }

    /// <summary>Searches transactions with filters and pagination</summary>
    public async Task<TransactionListDto> SearchAsync(TransactionSearchDto searchDto)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var (transactions, totalCount) = await _transactionRepository.SearchAsync(
            searchDto.AccountId,
            searchDto.StartDate,
            searchDto.EndDate,
            searchDto.Type,
            searchDto.PageNumber,
            searchDto.PageSize,
            businessId);

        return new TransactionListDto
        {
            Transactions = transactions.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    /// <summary>Gets a transaction by ID</summary>
    public async Task<TransactionDto> GetByIdAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var transaction = await _transactionRepository.GetByIdAsync(id, businessId);
        if (transaction == null)
            throw new KeyNotFoundException($"Transaction with ID {id} not found");

        return MapToDto(transaction);
    }

    /// <summary>Creates a transaction and updates the account balance</summary>
    public async Task<TransactionDto> CreateAsync(CreateTransactionDto dto)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var account = await _accountRepository.GetByIdAsync(dto.AccountId, businessId);
        if (account == null)
            throw new KeyNotFoundException($"Account with ID {dto.AccountId} not found");

        var validTypes = new[] { "debit", "credit" };
        if (!validTypes.Contains(dto.Type.ToLower()))
            throw new InvalidOperationException($"Invalid transaction type '{dto.Type}'. Must be 'debit' or 'credit'");

        var transaction = new Transaction
        {
            AccountId = dto.AccountId,
            Amount = dto.Amount,
            Type = dto.Type.ToLower(),
            Description = dto.Description,
            TransactionDate = dto.TransactionDate ?? DateTime.UtcNow,
            ReferenceType = dto.ReferenceType,
            ReferenceId = dto.ReferenceId
        };

        var created = await _transactionRepository.CreateAsync(transaction);

        // Update account balance: credits increase balance, debits decrease it
        account.Balance += dto.Type.ToLower() == "credit" ? dto.Amount : -dto.Amount;
        await _accountRepository.UpdateAsync(account);

        _logger.LogInformation("Transaction {TransactionId} created for account {AccountId}", created.Id, dto.AccountId);
        return MapToDto(created);
    }

    /// <summary>Gets all transactions for a specific account (ledger)</summary>
    public async Task<IEnumerable<TransactionDto>> GetLedgerAsync(long accountId)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var account = await _accountRepository.GetByIdAsync(accountId, businessId);
        if (account == null)
            throw new KeyNotFoundException($"Account with ID {accountId} not found");

        var transactions = await _transactionRepository.GetByAccountAsync(accountId, businessId);
        return transactions.Select(MapToDto);
    }

    private static TransactionDto MapToDto(Transaction t) => new TransactionDto
    {
        Id = t.Id,
        AccountId = t.AccountId,
        AccountName = t.Account?.Name ?? string.Empty,
        Amount = t.Amount,
        Type = t.Type,
        Description = t.Description,
        TransactionDate = t.TransactionDate,
        ReferenceType = t.ReferenceType,
        ReferenceId = t.ReferenceId
    };
}
