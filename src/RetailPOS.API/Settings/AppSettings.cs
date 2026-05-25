namespace RetailPOS.API.Settings;

public class CompanySettings
{
    public string CompanyName { get; set; } = "Retail POS";
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;

    // Legacy fields (kept for backwards compatibility with existing system-settings.json).
    public string Currency { get; set; } = "BDT";
    public string CurrencySymbol { get; set; } = "৳";

    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string TimeZone { get; set; } = "Asia/Dhaka";
}

public class CurrencySettings
{
    public string CurrencyCode { get; set; } = "BDT";
    public string CurrencySymbol { get; set; } = "৳";
    public string CurrencyName { get; set; } = "Bangladeshi Taka";
    public string SymbolPosition { get; set; } = "before";
    public int DecimalPlaces { get; set; } = 2;
    public string ThousandsSeparator { get; set; } = ",";
    public string DecimalSeparator { get; set; } = ".";
}

public class TaxSettings
{
    public bool TaxEnabled { get; set; } = true;
    public decimal DefaultTaxRate { get; set; } = 10;
    public string TaxName { get; set; } = "VAT";
    public bool PricesIncludeTax { get; set; } = false;
}

public class ReceiptSettings
{
    public string Header { get; set; } = "Thank you for shopping!";
    public string Footer { get; set; } = "Please come again";
    public bool ShowLogo { get; set; } = true;
    public bool ShowBarcode { get; set; } = false;
    public string PaperSize { get; set; } = "80mm";
}

public class InventorySettings
{
    public int LowStockThreshold { get; set; } = 10;
    public bool EnableLowStockAlerts { get; set; } = true;
    public bool AllowNegativeStock { get; set; } = false;
    public bool AutoReorder { get; set; } = false;
}

public class SystemSettings
{
    public CompanySettings Company { get; set; } = new();
    public CurrencySettings Currency { get; set; } = new();
    public TaxSettings Tax { get; set; } = new();
    public ReceiptSettings Receipt { get; set; } = new();
    public InventorySettings Inventory { get; set; } = new();
}
