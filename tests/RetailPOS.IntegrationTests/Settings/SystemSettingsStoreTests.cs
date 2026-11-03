using Microsoft.Extensions.Configuration;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Infrastructure.Settings;

namespace RetailPOS.IntegrationTests.Settings;

public sealed class SystemSettingsStoreTests
{
    [Fact]
    public async Task MissingFile_CreatesDefaultsInLegacyJsonShape()
    {
        var (store, directory, path) = CreateStore();
        try
        {
            var settings = await store.GetAsync(CancellationToken.None);

            Assert.True(File.Exists(path));
            Assert.Equal("Retail POS", settings.Company.CompanyName);
            Assert.Equal("BDT", settings.Currency.CurrencyCode);
            Assert.Equal(10, settings.Tax.DefaultTaxRate);
            Assert.Equal("INV", settings.InvoiceNumber.Prefix);
            Assert.Equal(15, settings.Pos.MaxCashSessionDurationHours);

            var persisted = await File.ReadAllTextAsync(path);
            Assert.Contains("\"Company\"", persisted);
            Assert.Contains("\"Currency\"", persisted);
            Assert.Contains("\"InvoiceNumber\"", persisted);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CurrencyUpdate_MirrorsLegacyCompanyFieldsAndSurvivesStoreRestart()
    {
        var (store, directory, path) = CreateStore();
        try
        {
            await store.GetAsync(CancellationToken.None);
            var updated = await store.UpdateCurrencyAsync(new CurrencySettings
            {
                CurrencyCode = "EUR",
                CurrencySymbol = "€",
                CurrencyName = "Euro",
                SymbolPosition = "after",
                DecimalPlaces = 3,
                ThousandsSeparator = ".",
                DecimalSeparator = ","
            }, CancellationToken.None);

            Assert.Equal("EUR", updated.Currency.CurrencyCode);
            Assert.Equal("EUR", updated.Company.Currency);
            Assert.Equal("€", updated.Company.CurrencySymbol);

            var restarted = CreateStore(path).Store;
            var reloaded = await restarted.GetAsync(CancellationToken.None);
            Assert.Equal("EUR", reloaded.Currency.CurrencyCode);
            Assert.Equal("EUR", reloaded.Company.Currency);
            Assert.Equal(3, reloaded.Currency.DecimalPlaces);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyCompanyCurrency_IsBackfilledIntoCurrencyBlock()
    {
        var (store, directory, path) = CreateStore();
        try
        {
            await File.WriteAllTextAsync(path,
                """{"Company":{"Currency":"CAD","CurrencySymbol":"$"},"Currency":{"CurrencyCode":"","CurrencySymbol":""}}""");

            var settings = await store.GetAsync(CancellationToken.None);

            Assert.Equal("CAD", settings.Currency.CurrencyCode);
            Assert.Equal("$", settings.Currency.CurrencySymbol);
            Assert.Equal("CAD", settings.Company.Currency);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StoreInstance_CachesLoadedSettingsUntilRestart()
    {
        var (store, directory, path) = CreateStore();
        try
        {
            var loaded = await store.GetAsync(CancellationToken.None);
            loaded.Company.CompanyName = "Cached";
            await File.WriteAllTextAsync(path, """{"Company":{"CompanyName":"External"} }""");

            var cached = await store.GetAsync(CancellationToken.None);
            var restarted = CreateStore(path).Store;
            var reloaded = await restarted.GetAsync(CancellationToken.None);

            Assert.Same(loaded, cached);
            Assert.Equal("Cached", cached.Company.CompanyName);
            Assert.Equal("External", reloaded.Company.CompanyName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentInvoiceAllocation_IsSerializedAndPersistsNextNumber()
    {
        var (store, directory, path) = CreateStore();
        try
        {
            await store.UpdateInvoiceNumberAsync(new InvoiceNumberSettings
            {
                Prefix = "ret",
                IncludeDate = true,
                NextNumber = 41,
                Padding = 4,
                Separator = "-"
            }, CancellationToken.None);

            var date = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
            var allocator = (ISystemSettingsStore)store;
            var allocated = await Task.WhenAll(
                Enumerable.Range(0, 20)
                    .Select(_ => allocator.AllocateNextInvoiceNumberAsync(date, CancellationToken.None)));

            Assert.Equal(20, allocated.Distinct().Count());
            Assert.Equal(
                Enumerable.Range(41, 20).Select(number => $"RET-20261006-{number:0000}").Order(),
                allocated.Order());

            var restarted = (ISystemSettingsStore)CreateStore(path).Store;
            var next = await restarted.AllocateNextInvoiceNumberAsync(date, CancellationToken.None);
            Assert.Equal("RET-20261006-0061", next);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledOperation_PropagatesCancellation()
    {
        var (store, directory, _) = CreateStore();
        try
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.GetAsync(cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (ISystemSettingsStore Store, string Directory, string FilePath) CreateStore(
        string? filePath = null)
    {
        var directory = filePath is null
            ? Path.Combine(Path.GetTempPath(), $"RetailPOS_Settings_{Guid.NewGuid():N}")
            : Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var path = filePath ?? Path.Combine(directory, "system-settings.json");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SystemSettings:FilePath"] = path
            })
            .Build();

        return (new JsonSystemSettingsStore(configuration), directory, path);
    }
}
