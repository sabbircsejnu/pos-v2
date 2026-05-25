using RetailPOS.API.Settings;
using System.Text.Json;

namespace RetailPOS.API.Services;

/// <summary>
/// Singleton service that persists system settings to a JSON file with in-memory caching.
/// Registered as singleton to safely share state and the semaphore across requests.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private SystemSettings? _cachedSettings;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(IWebHostEnvironment env)
    {
        _settingsFilePath = Path.Combine(env.ContentRootPath, "system-settings.json");
    }

    public async Task<SystemSettings> GetAllSettingsAsync() => await LoadSettingsAsync();
    public async Task<CompanySettings> GetCompanySettingsAsync() => (await LoadSettingsAsync()).Company;
    public async Task<CurrencySettings> GetCurrencySettingsAsync() => (await LoadSettingsAsync()).Currency;
    public async Task<TaxSettings> GetTaxSettingsAsync() => (await LoadSettingsAsync()).Tax;
    public async Task<ReceiptSettings> GetReceiptSettingsAsync() => (await LoadSettingsAsync()).Receipt;
    public async Task<InventorySettings> GetInventorySettingsAsync() => (await LoadSettingsAsync()).Inventory;

    private async Task<SystemSettings> LoadSettingsAsync()
    {
        if (_cachedSettings != null) return _cachedSettings;

        await _lock.WaitAsync();
        try
        {
            if (_cachedSettings != null) return _cachedSettings;
            _cachedSettings = await ReadFromFileAsync();
            return _cachedSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Reads settings from file without acquiring the lock (call only while holding _lock).
    private async Task<SystemSettings> ReadFromFileAsync()
    {
        if (File.Exists(_settingsFilePath))
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath);
            var loaded = JsonSerializer.Deserialize<SystemSettings>(json, _jsonOptions) ?? new SystemSettings();
            BackfillCurrencyFromCompany(loaded);
            return loaded;
        }

        var defaults = new SystemSettings();
        await WriteToFileAsync(defaults);
        return defaults;
    }

    // Migrates legacy company-level currency fields into the dedicated currency block when needed.
    private static void BackfillCurrencyFromCompany(SystemSettings settings)
    {
        if (settings.Currency == null)
        {
            settings.Currency = new CurrencySettings();
        }

        if (string.IsNullOrWhiteSpace(settings.Currency.CurrencyCode) && !string.IsNullOrWhiteSpace(settings.Company.Currency))
        {
            settings.Currency.CurrencyCode = settings.Company.Currency;
        }

        if (string.IsNullOrWhiteSpace(settings.Currency.CurrencySymbol) && !string.IsNullOrWhiteSpace(settings.Company.CurrencySymbol))
        {
            settings.Currency.CurrencySymbol = settings.Company.CurrencySymbol;
        }
    }

    // Writes settings to file without acquiring the lock (call only while holding _lock).
    private async Task WriteToFileAsync(SystemSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json);
    }

    public async Task<SystemSettings> UpdateCompanySettingsAsync(CompanySettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var current = _cachedSettings ?? await ReadFromFileAsync();
            current.Company = settings;
            await WriteToFileAsync(current);
            _cachedSettings = current;
            return current;
        }
        finally { _lock.Release(); }
    }

    public async Task<SystemSettings> UpdateCurrencySettingsAsync(CurrencySettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var current = _cachedSettings ?? await ReadFromFileAsync();
            current.Currency = settings;
            // Mirror back to legacy company fields for any consumers still reading them.
            current.Company.Currency = settings.CurrencyCode;
            current.Company.CurrencySymbol = settings.CurrencySymbol;
            await WriteToFileAsync(current);
            _cachedSettings = current;
            return current;
        }
        finally { _lock.Release(); }
    }

    public async Task<SystemSettings> UpdateTaxSettingsAsync(TaxSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var current = _cachedSettings ?? await ReadFromFileAsync();
            current.Tax = settings;
            await WriteToFileAsync(current);
            _cachedSettings = current;
            return current;
        }
        finally { _lock.Release(); }
    }

    public async Task<SystemSettings> UpdateReceiptSettingsAsync(ReceiptSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var current = _cachedSettings ?? await ReadFromFileAsync();
            current.Receipt = settings;
            await WriteToFileAsync(current);
            _cachedSettings = current;
            return current;
        }
        finally { _lock.Release(); }
    }

    public async Task<SystemSettings> UpdateInventorySettingsAsync(InventorySettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var current = _cachedSettings ?? await ReadFromFileAsync();
            current.Inventory = settings;
            await WriteToFileAsync(current);
            _cachedSettings = current;
            return current;
        }
        finally { _lock.Release(); }
    }
}
