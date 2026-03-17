using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Account;

public class AccountDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}

public class CreateAccountDto
{
    [Required] public string Name { get; set; } = string.Empty;
    [Required] public string Type { get; set; } = string.Empty; // asset, liability, expense, revenue
}

public class UpdateAccountDto
{
    [Required] public string Name { get; set; } = string.Empty;
    [Required] public string Type { get; set; } = string.Empty;
}

public class AccountListDto
{
    public List<AccountDto> Accounts { get; set; } = new();
    public int TotalCount { get; set; }
}
