using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;

// Corrects uncosted stock receipts from their GRN evidence. Previews by default and only appends when told exactly
// how many corrections the operator reviewed. It runs against a database on this machine only: Staging and Production
// corrections must go through a release process.
//
//   dotnet run --project tools/RetailPOS.StockCostCorrection -- --business 1
//   dotnet run --project tools/RetailPOS.StockCostCorrection -- --business 1 --apply --actor 5 --confirm-count 25
//
// Optional: --connection-env NAME reads the connection string from that environment variable instead of the API's
// Development configuration. The connection string is never printed.

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var options = ParseArguments(args);
if (!long.TryParse(options.GetValueOrDefault("business"), out var businessId))
    return Fail("--business <id> is required.");

var connectionString = options.TryGetValue("connection-env", out var variable)
    ? Environment.GetEnvironmentVariable(variable)
    : new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "src", "RetailPOS.API", "appsettings.Development.json"), optional: true)
        .AddUserSecrets<Marker>(optional: true)
        .Build()
        .GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    return Fail("No connection string was found. Run this from the repository root.");

var target = new NpgsqlConnectionStringBuilder(connectionString);
if (target.Host is not ("127.0.0.1" or "localhost" or "::1"))
    return Fail("This tool only runs against a database on this machine.");

await using var dbContext = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
    .UseNpgsql(connectionString)
    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
    .Options);
var service = new StockCostCorrectionService(
    dbContext,
    new StockLedgerWriter(dbContext, NullLogger<StockLedgerWriter>.Instance));

Console.WriteLine($"Database: {target.Database} on port {target.Port}. Business: {businessId}.");
var preview = await service.PreviewAsync(businessId, CancellationToken.None);

Console.WriteLine();
Console.WriteLine($"Corrections due: {preview.Plan.Corrections.Count}");
foreach (var item in preview.Plan.Corrections)
{
    var movement = item.Movement;
    Console.WriteLine(
        $"  row {movement.Id,5}  {movement.LocationType}:{movement.LocationId}  variant {movement.VariantId,4}  "
        + $"{movement.TransactionType,-13} qty {movement.QtyIn + movement.QtyOut,5}  value {Money(item.Value),14}  {item.Basis}");
}

Console.WriteLine();
Console.WriteLine($"Left unchanged: {preview.Plan.Skipped.Count}");
foreach (var item in preview.Plan.Skipped)
{
    Console.WriteLine($"  row {item.Movement.Id,5}  {item.Movement.LocationType}:{item.Movement.LocationId}  variant {item.Movement.VariantId,4}  {item.Reason}");
}

PrintSummary("Before", preview.Before);
PrintSummary("After", preview.After);
Console.WriteLine();
Console.WriteLine($"Inventory GL balance: {Money(preview.InventoryGlBalance)}");

if (!options.ContainsKey("apply"))
{
    Console.WriteLine();
    Console.WriteLine("Preview only. Nothing was written.");
    return 0;
}

if (!long.TryParse(options.GetValueOrDefault("actor"), out var actorId)
    || !int.TryParse(options.GetValueOrDefault("confirm-count"), out var confirmCount))
{
    return Fail("--apply needs --actor <userId> and --confirm-count <number shown in the preview>.");
}

dbContext.ChangeTracker.Clear();
var applied = await service.ApplyAsync(businessId, actorId, confirmCount, CancellationToken.None);
Console.WriteLine();
Console.WriteLine($"Applied {applied} correction(s).");
return 0;

static string Money(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);

static void PrintSummary(string title, IReadOnlyList<StockCostLocationSummary> rows)
{
    Console.WriteLine();
    Console.WriteLine($"{title}:");
    foreach (var row in rows)
    {
        Console.WriteLine(
            $"  {row.LocationType}:{row.LocationId,-3} {row.Records,3} records {row.Quantity,6} units | valued {row.ValuedRecords,3} / {row.ValuedQuantity,6} / {Money(row.Value),14} | "
            + $"zero cost {row.ZeroCostRecords,2} | not valued {row.UnvaluedRecords,3} / {row.UnvaluedQuantity,6}");
    }

    Console.WriteLine(
        $"  Total: {rows.Sum(row => row.Quantity)} units, valued {rows.Sum(row => row.ValuedQuantity)} at {Money(rows.Sum(row => row.Value))}, "
        + $"not valued {rows.Sum(row => row.UnvaluedQuantity)} ({rows.Sum(row => row.UnvaluedRecords)} records)");
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static Dictionary<string, string> ParseArguments(string[] arguments)
{
    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!arguments[index].StartsWith("--", StringComparison.Ordinal))
            continue;

        var name = arguments[index][2..];
        var hasValue = index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal);
        parsed[name] = hasValue ? arguments[++index] : string.Empty;
    }

    return parsed;
}

internal sealed class Marker;
