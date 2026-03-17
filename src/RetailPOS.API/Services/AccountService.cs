using RetailPOS.API.DTOs.Account;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Account business logic
/// </summary>
public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<AccountService> _logger;

    public AccountService(IAccountRepository accountRepository, ILogger<AccountService> logger)
    {
        _accountRepository = accountRepository;
        _logger = logger;
    }

    /// <summary>Gets all accounts, optionally filtered by type</summary>
    public async Task<AccountListDto> GetAllAsync(string? type = null)
    {
        var accounts = await _accountRepository.GetAllAsync(type);
        var list = accounts.ToList();

        return new AccountListDto
        {
            Accounts = list.Select(MapToDto).ToList(),
            TotalCount = list.Count
        };
    }

    /// <summary>Gets an account by ID including its computed balance</summary>
    public async Task<AccountDto> GetByIdAsync(long id)
    {
        var account = await _accountRepository.GetByIdAsync(id);
        if (account == null)
            throw new KeyNotFoundException($"Account with ID {id} not found");

        var dto = MapToDto(account);
        dto.Balance = await _accountRepository.GetBalanceAsync(id);
        return dto;
    }

    /// <summary>Creates a new chart-of-accounts entry</summary>
    public async Task<AccountDto> CreateAsync(CreateAccountDto dto)
    {
        var validTypes = new[] { "asset", "liability", "expense", "revenue" };
        if (!validTypes.Contains(dto.Type.ToLower()))
            throw new InvalidOperationException($"Invalid account type '{dto.Type}'. Must be one of: {string.Join(", ", validTypes)}");

        var account = new Account
        {
            Name = dto.Name.Trim(),
            Type = dto.Type.ToLower(),
            Balance = 0
        };

        var created = await _accountRepository.CreateAsync(account);
        _logger.LogInformation("Account {AccountId} created", created.Id);
        return MapToDto(created);
    }

    /// <summary>Updates an account's name and type</summary>
    public async Task<AccountDto> UpdateAsync(long id, UpdateAccountDto dto)
    {
        var account = await _accountRepository.GetByIdAsync(id);
        if (account == null)
            throw new KeyNotFoundException($"Account with ID {id} not found");

        var validTypes = new[] { "asset", "liability", "expense", "revenue" };
        if (!validTypes.Contains(dto.Type.ToLower()))
            throw new InvalidOperationException($"Invalid account type '{dto.Type}'. Must be one of: {string.Join(", ", validTypes)}");

        account.Name = dto.Name.Trim();
        account.Type = dto.Type.ToLower();

        var updated = await _accountRepository.UpdateAsync(account);
        _logger.LogInformation("Account {AccountId} updated", id);
        return MapToDto(updated);
    }

    /// <summary>Deletes an account if it has no transactions</summary>
    public async Task DeleteAsync(long id)
    {
        var account = await _accountRepository.GetByIdAsync(id);
        if (account == null)
            throw new KeyNotFoundException($"Account with ID {id} not found");

        if (await _accountRepository.HasTransactionsAsync(id))
            throw new InvalidOperationException("Cannot delete account with existing transactions");

        await _accountRepository.DeleteAsync(id);
        _logger.LogInformation("Account {AccountId} deleted", id);
    }

    private static AccountDto MapToDto(Account a) => new AccountDto
    {
        Id = a.Id,
        Name = a.Name,
        Type = a.Type,
        Balance = a.Balance
    };
}
