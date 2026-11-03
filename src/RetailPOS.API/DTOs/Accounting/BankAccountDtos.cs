namespace RetailPOS.API.DTOs.Accounting;

public sealed class BankAccountDto
{
    public long? Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AccountReference { get; set; }
    public long ChartAccountId { get; set; }
    public long? OutletId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ReconciliationStartDate { get; set; }
    public decimal? OpeningStatementBalance { get; set; }
}
