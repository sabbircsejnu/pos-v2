using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Infrastructure.Settings;

public sealed class JsonSystemSettingsStore : ISystemSettingsStore, ICompanyTimeZoneSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SystemSettings? _cachedSettings;

    public JsonSystemSettingsStore(IConfiguration configuration)
    {
        var configuredPath = configuration["SystemSettings:FilePath"];
        var environmentPath = Environment.GetEnvironmentVariable("RETAILPOS_SYSTEM_SETTINGS_PATH");
        _settingsFilePath = !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : !string.IsNullOrWhiteSpace(environmentPath)
                ? environmentPath
                : Path.Combine(Directory.GetCurrentDirectory(), "system-settings.json");
    }

    public Task<SystemSettings> GetAsync(CancellationToken cancellationToken) =>
        LoadSettingsAsync(cancellationToken);

    public string GetCompanyTimeZoneId()
    {
        if (_cachedSettings is not null)
            return _cachedSettings.Company.TimeZone;

        return File.Exists(_settingsFilePath)
            ? (JsonSerializer.Deserialize<SystemSettings>(File.ReadAllText(_settingsFilePath), JsonOptions)
                ?? new SystemSettings()).Company.TimeZone
            : new SystemSettings().Company.TimeZone;
    }

    public Task<SystemSettings> UpdateCompanyAsync(
        CompanySettings settings,
        CancellationToken cancellationToken) =>
        UpdateAsync(current => current.Company = settings, cancellationToken);

    public Task<SystemSettings> UpdateCurrencyAsync(
        CurrencySettings settings,
        CancellationToken cancellationToken) =>
        UpdateAsync(current =>
        {
            current.Currency = settings;
            current.Company.Currency = settings.CurrencyCode;
            current.Company.CurrencySymbol = settings.CurrencySymbol;
        }, cancellationToken);

    public Task<SystemSettings> UpdateTaxAsync(
        TaxSettings settings,
        CancellationToken cancellationToken) =>
        UpdateAsync(current => current.Tax = settings, cancellationToken);

    public Task<SystemSettings> UpdateReceiptAsync(
        ReceiptSettings settings,
        CancellationToken cancellationToken) =>
        UpdateAsync(current => current.Receipt = settings, cancellationToken);

    public Task<SystemSettings> UpdateInvoiceNumberAsync(
        InvoiceNumberSettings settings,
        CancellationToken cancellationToken) =>
        UpdateAsync(current => current.InvoiceNumber = settings, cancellationToken);

    public Task<SystemSettings> UpdateInventoryAsync(
        InventorySettings settings,
        CancellationToken cancellationToken) =>
        UpdateAsync(current => current.Inventory = settings, cancellationToken);

    public async Task<string> AllocateNextInvoiceNumberAsync(
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var current = _cachedSettings ?? await ReadFromFileAsync(cancellationToken);
            var invoice = current.InvoiceNumber ?? new InvoiceNumberSettings();
            var currentNumber = Math.Max(1, invoice.NextNumber);
            var generated = FormatInvoiceNumber(invoice, utcNow, currentNumber);

            invoice.NextNumber = currentNumber + 1;
            current.InvoiceNumber = invoice;

            await WriteToFileAsync(current, cancellationToken);
            _cachedSettings = current;
            return generated;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SystemSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        if (_cachedSettings is not null)
            return _cachedSettings;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedSettings is not null)
                return _cachedSettings;

            _cachedSettings = await ReadFromFileAsync(cancellationToken);
            return _cachedSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SystemSettings> UpdateAsync(
        Action<SystemSettings> update,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var current = _cachedSettings ?? await ReadFromFileAsync(cancellationToken);
            update(current);
            await WriteToFileAsync(current, cancellationToken);
            _cachedSettings = current;
            return current;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SystemSettings> ReadFromFileAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_settingsFilePath))
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
            var loaded = JsonSerializer.Deserialize<SystemSettings>(json, JsonOptions) ?? new SystemSettings();
            BackfillCurrencyFromCompany(loaded);
            return loaded;
        }

        var defaults = new SystemSettings();
        await WriteToFileAsync(defaults, cancellationToken);
        return defaults;
    }

    private static void BackfillCurrencyFromCompany(SystemSettings settings)
    {
        if (settings.Currency is null)
            settings.Currency = new CurrencySettings();

        if (string.IsNullOrWhiteSpace(settings.Currency.CurrencyCode)
            && !string.IsNullOrWhiteSpace(settings.Company.Currency))
        {
            settings.Currency.CurrencyCode = settings.Company.Currency;
        }

        if (string.IsNullOrWhiteSpace(settings.Currency.CurrencySymbol)
            && !string.IsNullOrWhiteSpace(settings.Company.CurrencySymbol))
        {
            settings.Currency.CurrencySymbol = settings.Company.CurrencySymbol;
        }
    }

    private async Task WriteToFileAsync(
        SystemSettings settings,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);
    }

    private static string FormatInvoiceNumber(
        InvoiceNumberSettings settings,
        DateTime utcNow,
        int number)
    {
        var prefix = string.IsNullOrWhiteSpace(settings.Prefix)
            ? "INV"
            : settings.Prefix.Trim().ToUpperInvariant();
        var separator = string.IsNullOrWhiteSpace(settings.Separator)
            ? "-"
            : settings.Separator.Trim();
        var padding = Math.Clamp(settings.Padding, 3, 10);
        var padded = number.ToString().PadLeft(padding, '0');

        return settings.IncludeDate
            ? $"{prefix}{separator}{utcNow:yyyyMMdd}{separator}{padded}"
            : $"{prefix}{separator}{padded}";
    }
}
