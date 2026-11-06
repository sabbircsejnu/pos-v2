using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleReceiptSettingsProvider : ISaleReceiptSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsFilePath;

    public SaleReceiptSettingsProvider(IConfiguration configuration)
    {
        var configuredPath = configuration["SystemSettings:FilePath"];
        var environmentPath = Environment.GetEnvironmentVariable("RETAILPOS_SYSTEM_SETTINGS_PATH");
        _settingsFilePath = !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : !string.IsNullOrWhiteSpace(environmentPath)
                ? environmentPath
                : Path.Combine(Directory.GetCurrentDirectory(), "system-settings.json");
    }

    public async Task<SaleReceiptSettingsReadModel> GetAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return ToReadModel(new StoredSystemSettings());
        }

        await using var stream = File.OpenRead(_settingsFilePath);
        var settings = await JsonSerializer.DeserializeAsync<StoredSystemSettings>(
            stream,
            JsonOptions,
            cancellationToken);

        return ToReadModel(settings ?? new StoredSystemSettings());
    }

    private static SaleReceiptSettingsReadModel ToReadModel(StoredSystemSettings settings) =>
        new(
            settings.Receipt.DefaultReceiptSize,
            settings.Receipt.PaperSize,
            settings.Receipt.ShowLogo,
            settings.Receipt.ShowBarcode,
            settings.Receipt.ShowQr,
            settings.Receipt.Header,
            settings.Receipt.ThankYouMessage,
            settings.Receipt.ReturnPolicy,
            settings.Receipt.Footer,
            settings.Company.CompanyName,
            settings.Company.LogoBase64,
            settings.Company.Phone,
            settings.Company.TaxNumber)
        {
            DateFormat = settings.Company.DateFormat,
            ConcernOf = settings.Company.ConcernOf,
            Website = settings.Company.Website,
            CurrencyCode = settings.Currency.CurrencyCode,
            CurrencyName = settings.Currency.CurrencyName
        };

    private sealed class StoredSystemSettings
    {
        public StoredCompanySettings Company { get; set; } = new();
        public StoredReceiptSettings Receipt { get; set; } = new();
        public StoredCurrencySettings Currency { get; set; } = new();
    }

    private sealed class StoredCurrencySettings
    {
        public string CurrencyCode { get; set; } = "BDT";
        public string CurrencyName { get; set; } = string.Empty;
    }

    private sealed class StoredCompanySettings
    {
        public string CompanyName { get; set; } = "Retail POS";
        public string ConcernOf { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string TaxNumber { get; set; } = string.Empty;
        public string LogoBase64 { get; set; } = string.Empty;
        public string DateFormat { get; set; } = "dd/MM/yyyy";
    }

    private sealed class StoredReceiptSettings
    {
        public string Header { get; set; } = "Thank you for shopping!";
        public string Footer { get; set; } = "Please come again";
        public bool ShowLogo { get; set; } = true;
        public bool ShowBarcode { get; set; }
        public bool ShowQr { get; set; }
        public string DefaultReceiptSize { get; set; } = "80mm";
        public string PaperSize { get; set; } = "80mm";
        public string ThankYouMessage { get; set; } = "Thank you for your purchase!";
        public string ReturnPolicy { get; set; } = "Items can be returned according to store policy.";
    }
}
