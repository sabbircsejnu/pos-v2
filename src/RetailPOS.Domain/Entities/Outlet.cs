namespace RetailPOS.Core.Entities;

public class Outlet
{
    public long Id { get; set; }
    public long? BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? ContactNumber { get; set; }

    /// <summary>D13 invoice prefix (e.g. "DHK01"); unique per Business and locked once an invoice has been issued with it.</summary>
    public string? InvoicePrefix { get; set; }

    /// <summary>Optional IANA time zone overriding the Business default (D9).</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>Branch name printed on invoices; the outlet name is printed when empty.</summary>
    public string? InvoicePrintBranchName { get; set; }

    /// <summary>Business Identification Number printed on this outlet's invoices.</summary>
    public string? BinNumber { get; set; }

    /// <summary>Mushak form reference printed beside the BIN (e.g. "6.3").</summary>
    public string? Mushak { get; set; }

    /// <summary>Multiline return/exchange policy printed on this outlet's invoices.</summary>
    public string? ReturnExchangePolicy { get; set; }

    /// <summary>Multiline VAT law notification printed on this outlet's invoices.</summary>
    public string? VatLawNotification { get; set; }
    public long? ManagerId { get; set; }
    public long SourceVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Business? Business { get; set; }
    public virtual User? Manager { get; set; }
    public virtual ICollection<User> Users { get; set; } = new List<User>();
    public virtual ICollection<PosTerminal> PosTerminals { get; set; } = new List<PosTerminal>();
    public virtual ICollection<CashSession> CashSessions { get; set; } = new List<CashSession>();
    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public virtual ICollection<OutletSalesTargetVersion> SalesTargetVersions { get; set; } = new List<OutletSalesTargetVersion>();
    public virtual ICollection<OutletSalesTargetOverride> SalesTargetOverrides { get; set; } = new List<OutletSalesTargetOverride>();
}
