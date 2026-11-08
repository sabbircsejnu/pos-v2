using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RetailPOS.Application.BusinessAdministration.Commands;
using RetailPOS.Application.Integration;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingConfigurations;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.BusinessAdministration;
using RetailPOS.Infrastructure.Data;

// Exchange browser-UAT environment (local only).
//   provision  create the UAT database if missing (after isolation checks), apply all migrations, seed synthetic data idempotently
//   api        start the built API (Release) against the UAT database on http://127.0.0.1:5105
//   status     print applied migrations and synthetic data counts
//   ids        write the non-secret synthetic entity ids to e2e/exchange-uat/.uat/ids.json (used by the browser tests)
//   reconcile  read-only database reconciliation of every exchange and reversal (stock, ledger, journals, cash, entitlements)
// Never drops, truncates or resets anything. Credentials are read at run time and never printed.

const string UatDatabase = "iranimart_pos_v2_exchange_uat";
const string UatMarker = "iranimart-exchange-uat:synthetic:persistent";
const int TestPostgresPort = 55432;
const int DevelopmentPostgresPort = 5435;
const string ApiUrl = "http://127.0.0.1:5105";
const string BusinessName = "Exchange UAT Business (synthetic)";
const string EmailDomain = "exchange-uat.test";

var command = args.FirstOrDefault() ?? "status";
var root = FindRepositoryRoot();
var secretsDirectory = Path.Combine(root, "e2e", "exchange-uat", ".uat");
var credentialsPath = Path.Combine(secretsDirectory, "credentials.json");

var uatConnection = BuildUatConnectionString();
switch (command)
{
    case "provision":
        await EnsureDatabaseAsync();
        await MigrateAsync();
        await SeedAsync();
        await StatusAsync();
        break;
    case "api":
        await StartApiAsync();
        break;
    case "status":
        await StatusAsync();
        break;
    case "ids":
        await WriteIdsAsync();
        break;
    case "reconcile":
        return await ReconcileAsync();
    case "stock":
    {
        // Read-only: on-hand quantity of one variant at one outlet (used by the browser tests).
        await using var db = CreateContext();
        var variantId = long.Parse(args[1]);
        var outletId = long.Parse(args[2]);
        var quantity = await db.Inventories.AsNoTracking()
            .Where(row => row.VariantId == variantId && row.LocationId == outletId && row.LocationType == "outlet")
            .SumAsync(row => (decimal?)row.Quantity) ?? 0m;
        Console.WriteLine(quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        break;
    }
    default:
        Console.Error.WriteLine("Usage: provision | api | status");
        return 2;
}

return 0;

string BuildUatConnectionString()
{
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets(typeof(Program).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();
    var baseConnection = configuration["RETAILPOS_TEST_POSTGRES"];
    if (string.IsNullOrWhiteSpace(baseConnection))
        throw new InvalidOperationException("RETAILPOS_TEST_POSTGRES (IntegrationTests user-secret) is not configured.");

    var builder = new NpgsqlConnectionStringBuilder(baseConnection);
    // Isolation guards: only the loopback A2/Test PostgreSQL server, never the Development port or database.
    if (builder.Port == DevelopmentPostgresPort || builder.Port != TestPostgresPort)
        throw new InvalidOperationException($"Refusing: the UAT database must live on the Test PostgreSQL port {TestPostgresPort}.");
    if (builder.Host is not ("127.0.0.1" or "localhost" or "::1"))
        throw new InvalidOperationException("Refusing: the UAT database must be on a loopback host.");
    builder.Database = UatDatabase;
    builder.Pooling = true;
    return builder.ConnectionString;
}

async Task EnsureDatabaseAsync()
{
    var admin = new NpgsqlConnectionStringBuilder(uatConnection) { Database = "postgres", Pooling = false };
    await using var connection = new NpgsqlConnection(admin.ConnectionString);
    await connection.OpenAsync();
    await using (var exists = new NpgsqlCommand(
        "SELECT shobj_description(oid, 'pg_database') FROM pg_database WHERE datname = @name", connection))
    {
        exists.Parameters.AddWithValue("name", UatDatabase);
        var found = await exists.ExecuteScalarAsync();
        if (found is not null)
        {
            var marker = found as string;
            if (!string.Equals(marker, UatMarker, StringComparison.Ordinal))
                throw new InvalidOperationException($"Refusing: database {UatDatabase} exists but is not marked as the synthetic UAT database.");
            Console.WriteLine($"UAT database {UatDatabase} exists (marker ok); reusing it.");
            return;
        }
    }

    await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{UatDatabase}\"", connection))
        await create.ExecuteNonQueryAsync();
    await using (var comment = new NpgsqlCommand($"COMMENT ON DATABASE \"{UatDatabase}\" IS '{UatMarker}'", connection))
        await comment.ExecuteNonQueryAsync();
    Console.WriteLine($"Created UAT database {UatDatabase}.");
}

RetailPOSDbContext CreateContext() =>
    new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(uatConnection).Options);

async Task MigrateAsync()
{
    await using var db = CreateContext();
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    Console.WriteLine($"Pending migrations: {pending.Count}");
    await db.Database.MigrateAsync();
    // The API applies this idempotent DDL on its own migrate path; keep the UAT schema identical.
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS product_variation_selected_options (
            id          BIGSERIAL    PRIMARY KEY,
            product_id  BIGINT       NOT NULL REFERENCES products(id)          ON DELETE CASCADE,
            variation_id BIGINT      NOT NULL REFERENCES variations(id)        ON DELETE CASCADE,
            option_id   BIGINT       NOT NULL REFERENCES variation_options(id) ON DELETE CASCADE,
            created_at  TIMESTAMP    NOT NULL DEFAULT NOW(),
            UNIQUE (product_id, variation_id, option_id)
        )");
}

string LoadOrCreatePassword()
{
    Directory.CreateDirectory(secretsDirectory);
    if (File.Exists(credentialsPath))
    {
        using var existing = JsonDocument.Parse(File.ReadAllText(credentialsPath));
        return existing.RootElement.GetProperty("password").GetString()!;
    }

    // A random synthetic password for the synthetic UAT users only; stored in a git-ignored local file.
    var password = "Uat-" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', 'x').Replace('/', 'y').TrimEnd('=') + "#9";
    File.WriteAllText(credentialsPath, JsonSerializer.Serialize(new
    {
        note = "Synthetic Exchange UAT users only (database iranimart_pos_v2_exchange_uat). Never reuse elsewhere. Git-ignored.",
        password
    }, new JsonSerializerOptions { WriteIndented = true }));
    return password;
}

async Task SeedAsync()
{
    var password = LoadOrCreatePassword();
    var hasher = new BCryptPasswordHasher();
    await using var db = CreateContext();

    var business = await db.Businesses.FirstOrDefaultAsync(row => row.Name == BusinessName);
    if (business is null)
    {
        var store = new BusinessOnboardingWriteStore(
            db,
            new AccountingConfigurationProvisioner(db, NullLogger<AccountingConfigurationProvisioner>.Instance),
            hasher,
            new NoAuditService(),
            new NoSourceEvents(),
            NullLogger<BusinessOnboardingWriteStore>.Instance);
        var result = await store.CreateBusinessAsync(new CreateBusinessCommand(
            BusinessName, $"business@{EmailDomain}", "0000000000", "Synthetic UAT address",
            "UAT Owner", $"uat.owner@{EmailDomain}",
            "UAT Manager A", $"uat.manager.a@{EmailDomain}",
            "UAT Sales A", $"uat.sales.a@{EmailDomain}",
            "UAT Accounts", $"uat.accounts@{EmailDomain}",
            "UAT Outlet A", false, null, null, null), CancellationToken.None);
        Console.WriteLine($"Onboarding: {result.Status}");
        business = await db.Businesses.SingleAsync(row => row.Name == BusinessName);
    }

    var businessId = business.Id;
    var outletA = await db.Outlets.SingleAsync(row => row.BusinessId == businessId && row.Name == "UAT Outlet A");
    outletA.InvoicePrefix ??= "UATA";
    var outletB = await db.Outlets.FirstOrDefaultAsync(row => row.BusinessId == businessId && row.Name == "UAT Outlet B");
    if (outletB is null)
    {
        outletB = new Outlet { BusinessId = businessId, Name = "UAT Outlet B", Address = "Synthetic UAT address B", InvoicePrefix = "UATB" };
        db.Outlets.Add(outletB);
    }
    await db.SaveChangesAsync();

    async Task EnsureTerminalAsync(long outletId, string code)
    {
        if (!await db.PosTerminals.AnyAsync(row => row.OutletId == outletId && row.Code == code))
            db.PosTerminals.Add(new PosTerminal { OutletId = outletId, Name = code, Code = code, IsActive = true, IsDefault = true });
        await db.SaveChangesAsync();
    }
    await EnsureTerminalAsync(outletA.Id, "UATA-T1");
    await EnsureTerminalAsync(outletB.Id, "UATB-T1");

    var managerRole = await db.Roles.SingleAsync(row => row.Name == "OutletManager");
    var salesRole = await db.Roles.SingleAsync(row => row.Name == "SalesPerson");
    // Keep the synthetic roles in line with the current onboarding templates (onboarding merges them the same way).
    foreach (var (role, template) in new[]
             {
                 (managerRole, RetailPOS.Application.BusinessAdministration.BusinessRolePermissions.OutletManager),
                 (salesRole, RetailPOS.Application.BusinessAdministration.BusinessRolePermissions.SalesPerson)
             })
    {
        var current = JsonSerializer.Deserialize<List<string>>(role.Permissions) ?? [];
        var missing = template.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count == 0)
            continue;
        role.Permissions = JsonSerializer.Serialize(current.Concat(missing).ToList());
        role.UpdatedAt = DateTime.UtcNow;
        Console.WriteLine($"Role {role.Name}: added {string.Join(", ", missing)}");
    }
    await db.SaveChangesAsync();
    async Task EnsureUserAsync(string name, string email, long roleId, long outletId)
    {
        if (await db.Users.AnyAsync(row => row.Email == email))
            return;
        db.Users.Add(new User { BusinessId = businessId, Name = name, Email = email, PasswordHash = hasher.Hash(password), RoleId = roleId, OutletId = outletId, IsActive = true });
        await db.SaveChangesAsync();
    }
    await EnsureUserAsync("UAT Manager B", $"uat.manager.b@{EmailDomain}", managerRole.Id, outletB.Id);
    await EnsureUserAsync("UAT Sales B", $"uat.sales.b@{EmailDomain}", salesRole.Id, outletB.Id);

    // Every synthetic user signs in with the synthetic password; no forced reset in UAT.
    var users = await db.Users.Where(row => row.BusinessId == businessId && row.Email.EndsWith("@" + EmailDomain)).ToListAsync();
    foreach (var user in users)
    {
        if (!hasher.Verify(password, user.PasswordHash))
            user.PasswordHash = hasher.Hash(password);
        user.MustResetPassword = false;
        user.IsActive = true;
    }
    await db.SaveChangesAsync();

    // Explicit POS settings (as an owner would save them): 10% VAT on top of prices, Asia/Dhaka business day.
    // Without a row the business inherits the legacy company-wide tax settings.
    if (!await db.BusinessPosSettings.AnyAsync(row => row.BusinessId == businessId))
        db.BusinessPosSettings.Add(new BusinessPosSettings
        {
            BusinessId = businessId, TaxEnabled = true, DefaultTaxRate = 10m, TaxName = "VAT", PricesIncludeTax = false,
            DefaultTimeZoneId = BusinessPosSettings.DefaultTimeZone, MaxManualDiscountPercent = 10m
        });

    if (!await db.Suppliers.AnyAsync(row => row.BusinessId == businessId))
        db.Suppliers.Add(new Supplier { BusinessId = businessId, Name = "UAT Supplier (synthetic)" });
    var category = await db.Categories.FirstOrDefaultAsync(row => row.BusinessId == businessId && row.Name == "UAT Apparel");
    if (category is null)
    {
        category = new Category { BusinessId = businessId, Name = "UAT Apparel", Description = "Synthetic UAT category" };
        db.Categories.Add(category);
    }
    if (!await db.Customers.AnyAsync(row => row.BusinessId == businessId && row.CustomerCode == "UAT-CUST-1"))
        db.Customers.Add(new Customer { BusinessId = businessId, Name = "UAT Registered Customer", CustomerCode = "UAT-CUST-1", Phone = "01000000001", IsActive = true });
    await db.SaveChangesAsync();

    var products = new (string Code, string Name, decimal Price, decimal Cost)[]
    {
        ("UAT-SHIRT", "UAT Shirt", 100m, 40m),
        ("UAT-JACKET", "UAT Jacket", 120m, 50m),
        ("UAT-CAP", "UAT Cap", 50m, 20m),
        ("UAT-COAT", "UAT Coat", 300m, 120m),
        ("UAT-WATCH", "UAT Watch", 600m, 250m)
    };
    foreach (var (code, name, price, cost) in products)
    {
        if (await db.Products.AnyAsync(row => row.BusinessId == businessId && row.ProductCode == code))
            continue;
        var product = new Product { BusinessId = businessId, Name = name, ProductCode = code, Sku = code, Barcode = code, CategoryId = category.Id, BasePrice = price, CostPrice = cost, Status = ProductStatus.Active };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductVariants.Add(new ProductVariant { BusinessId = businessId, ProductId = product.Id, Name = "Standard", Sku = code + "-STD", Barcode = code + "-STD" });
        await db.SaveChangesAsync();
    }

    // A multi-variant product found by its main product code (Exchange replacement search UAT).
    if (!await db.Products.AnyAsync(row => row.BusinessId == businessId && row.ProductCode == "ABA852P"))
    {
        var borka = new Product
        {
            BusinessId = businessId, Name = "Ari Wasenat Premium Borka", ProductCode = "ABA852P", CategoryId = category.Id,
            BasePrice = 2450m, CostPrice = 900m, HasVariants = true, Status = ProductStatus.Active
        };
        db.Products.Add(borka);
        await db.SaveChangesAsync();
        foreach (var (colour, size, adjustment) in new[] { ("Black", "M", 0m), ("Black", "L", 0m), ("Navy", "M", 0m), ("Navy", "L", 100m) })
        {
            var code = $"ABA852P-{colour[..3].ToUpperInvariant()}-{size}";
            db.ProductVariants.Add(new ProductVariant
            {
                BusinessId = businessId, ProductId = borka.Id, Name = $"{colour} / {size}", Sku = code, Barcode = $"UAT{code.Replace("-", "")}",
                Attributes = JsonSerializer.Serialize(new Dictionary<string, string> { ["Colour"] = colour, ["Size"] = size }), PriceAdjustment = adjustment
            });
        }
        await db.SaveChangesAsync();
    }

    Console.WriteLine("Synthetic UAT data is in place (idempotent).");
}

async Task StatusAsync()
{
    await using var db = CreateContext();
    var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
    Console.WriteLine($"Database: {UatDatabase} on Test PostgreSQL port {TestPostgresPort}");
    Console.WriteLine($"Applied migrations: {applied.Count}; last three: {string.Join(", ", applied.TakeLast(3))}");
    Console.WriteLine($"Pending migrations: {(await db.Database.GetPendingMigrationsAsync()).Count()}");
    var business = await db.Businesses.AsNoTracking().FirstOrDefaultAsync(row => row.Name == BusinessName);
    if (business is null)
    {
        Console.WriteLine("Synthetic business: not provisioned");
        return;
    }
    Console.WriteLine($"Business id {business.Id}; outlets {await db.Outlets.CountAsync(row => row.BusinessId == business.Id)}; " +
        $"users {await db.Users.CountAsync(row => row.BusinessId == business.Id)}; products {await db.Products.CountAsync(row => row.BusinessId == business.Id)}; " +
        $"sales {await db.Sales.CountAsync(row => row.BusinessId == business.Id)}; exchanges {await db.Exchanges.CountAsync(row => row.BusinessId == business.Id)}");
}

async Task StartApiAsync()
{
    var dll = Path.Combine(root, "src", "RetailPOS.API", "bin", "Release", "net10.0", "RetailPOS.API.dll");
    if (!File.Exists(dll))
        throw new InvalidOperationException("Build the API first: dotnet build src/RetailPOS.API -c Release");
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = Path.GetDirectoryName(dll)!,
        UseShellExecute = false
    };
    start.ArgumentList.Add(dll);
    // Non-secret overrides on the command line (highest precedence). An empty Redis string keeps the in-memory cache,
    // so the shared Development Redis is never touched; the broker and Elasticsearch stay off.
    foreach (var argument in new[]
             {
                 "--urls=" + ApiUrl,
                 "--ConnectionStrings:Redis=",
                 "--PosCache:InstanceName=exchange-uat:",
                 "--Database:MigrateOnStartup=false",
                 "--Database:SeedOnStartup=false",
                 "--DurableDelivery:Enabled=false",
                 "--Elasticsearch:Enabled=false"
             })
        start.ArgumentList.Add(argument);
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    // The UAT connection string (with the Test-server credentials) goes through the environment only, never argv.
    start.Environment["ConnectionStrings__DefaultConnection"] = uatConnection;
    Console.WriteLine($"Starting the API on {ApiUrl} against {UatDatabase} (Ctrl+C to stop)...");
    using var process = Process.Start(start)!;
    await process.WaitForExitAsync();
}

async Task WriteIdsAsync()
{
    await using var db = CreateContext();
    var business = await db.Businesses.AsNoTracking().SingleAsync(row => row.Name == BusinessName);
    var outlets = await db.Outlets.AsNoTracking().Where(row => row.BusinessId == business.Id).ToDictionaryAsync(row => row.Name, row => row.Id);
    var outletIds = outlets.Values.ToList();
    var terminals = await db.PosTerminals.AsNoTracking().Where(row => outletIds.Contains(row.OutletId)).ToDictionaryAsync(row => row.Code, row => row.Id);
    var variants = await db.ProductVariants.AsNoTracking().Where(row => row.BusinessId == business.Id).ToDictionaryAsync(row => row.Sku, row => row.Id);
    var users = await db.Users.AsNoTracking().Where(row => row.BusinessId == business.Id).ToDictionaryAsync(row => row.Email, row => row.Id);
    var customer = await db.Customers.AsNoTracking().Where(row => row.BusinessId == business.Id && row.CustomerCode == "UAT-CUST-1").Select(row => row.Id).SingleAsync();
    var supplier = await db.Suppliers.AsNoTracking().Where(row => row.BusinessId == business.Id).Select(row => row.Id).FirstAsync();
    Directory.CreateDirectory(secretsDirectory);
    var path = Path.Combine(secretsDirectory, "ids.json");
    File.WriteAllText(path, JsonSerializer.Serialize(new { businessId = business.Id, outlets, terminals, variants, users, customerId = customer, supplierId = supplier },
        new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote {path}");
}

async Task<int> ReconcileAsync()
{
    await using var db = CreateContext();
    var business = await db.Businesses.AsNoTracking().SingleAsync(row => row.Name == BusinessName);
    var businessId = business.Id;
    var failures = new List<string>();
    void Check(bool ok, string message) { if (!ok) failures.Add(message); }

    var exchanges = await db.Exchanges.AsNoTracking().Where(row => row.BusinessId == businessId)
        .Include(row => row.ReturnLines).Include(row => row.ReplacementLines).ToListAsync();
    var reversals = await db.ExchangeReversals.AsNoTracking().Where(row => row.BusinessId == businessId).ToListAsync();
    var exchangeIds = exchanges.Select(row => row.Id).ToList();
    var reversalIds = reversals.Select(row => row.Id).ToList();

    // 1. Every exchange and reversal journal exists and is balanced.
    var journalIds = exchanges.Select(row => row.JournalEntryId).Concat(reversals.Select(row => row.JournalEntryId)).OfType<long>().ToList();
    var journals = await db.JournalEntryLines.AsNoTracking().Where(line => journalIds.Contains(line.JournalEntryId))
        .GroupBy(line => line.JournalEntryId)
        .Select(group => new { Id = group.Key, Debit = group.Sum(line => line.DebitAmount), Credit = group.Sum(line => line.CreditAmount) }).ToListAsync();
    foreach (var journal in journals)
        Check(journal.Debit == journal.Credit, $"Journal {journal.Id} unbalanced: {journal.Debit} vs {journal.Credit}");
    Check(journals.Count == journalIds.Count && journalIds.Count == exchanges.Count + reversals.Count,
        $"Journals: {journals.Count} found for {exchanges.Count} exchanges and {reversals.Count} reversals");

    // 2. COGS on the stock ledger equals the recorded cost (the dashboard basis); a reversal leaves only retained packaging.
    var ledger = await db.StockLedgers.AsNoTracking()
        .Where(row => (row.ReferenceType == StockLedgerReferenceType.Exchange && exchangeIds.Contains(row.ReferenceId))
            || (row.ReferenceType == StockLedgerReferenceType.ExchangeReversal && reversalIds.Contains(row.ReferenceId)))
        .ToListAsync();
    decimal Cogs(string type, long id) => ledger.Where(row => row.ReferenceType == type && row.ReferenceId == id)
        .Sum(row => row.QtyOut > 0m ? row.MovementValue : -row.MovementValue);
    foreach (var exchange in exchanges)
        Check(Cogs(StockLedgerReferenceType.Exchange, exchange.Id) == exchange.ReplacementCost - exchange.ReturnCost,
            $"Exchange {exchange.ExchangeNumber}: ledger COGS {Cogs(StockLedgerReferenceType.Exchange, exchange.Id)} vs recorded {exchange.ReplacementCost - exchange.ReturnCost}");
    foreach (var reversal in reversals)
    {
        var net = Cogs(StockLedgerReferenceType.Exchange, reversal.ExchangeId) + Cogs(StockLedgerReferenceType.ExchangeReversal, reversal.Id);
        Check(net == reversal.RetainedPackagingCost, $"Reversal {reversal.ReversalNumber}: net ledger COGS {net} vs retained {reversal.RetainedPackagingCost}");
    }

    // 3. Inventory equals the stock ledger for every synthetic variant and outlet.
    var outletIds = await db.Outlets.AsNoTracking().Where(row => row.BusinessId == businessId).Select(row => row.Id).ToListAsync();
    var variantIds = await db.ProductVariants.AsNoTracking().Where(row => row.BusinessId == businessId).Select(row => row.Id).ToListAsync();
    var inventories = await db.Inventories.AsNoTracking()
        .Where(row => row.LocationType == "outlet" && outletIds.Contains(row.LocationId) && variantIds.Contains(row.VariantId)).ToListAsync();
    var ledgerTotals = await db.StockLedgers.AsNoTracking()
        .Where(row => row.LocationType == "outlet" && outletIds.Contains(row.LocationId) && variantIds.Contains(row.VariantId))
        .GroupBy(row => new { row.VariantId, row.LocationId })
        .Select(group => new { group.Key.VariantId, group.Key.LocationId, Net = group.Sum(row => row.QtyIn - row.QtyOut) }).ToListAsync();
    foreach (var inventory in inventories)
    {
        var net = ledgerTotals.SingleOrDefault(row => row.VariantId == inventory.VariantId && row.LocationId == inventory.LocationId)?.Net ?? 0m;
        Check(net == inventory.Quantity, $"Inventory variant {inventory.VariantId} outlet {inventory.LocationId}: on hand {inventory.Quantity} vs ledger {net}");
    }

    // 4. Shared return entitlement: returned quantity = exchange returns still in force (+ refunds, if any).
    var completed = exchanges.Where(row => row.Status == "completed").ToList();
    var saleItemIds = exchanges.SelectMany(row => row.ReturnLines).Where(line => line.SaleItemId != null).Select(line => line.SaleItemId!.Value).Distinct().ToList();
    var saleItems = await db.SaleItems.AsNoTracking().Where(row => saleItemIds.Contains(row.Id)).ToListAsync();
    var refundedSaleIds = await db.Set<RetailPOS.Core.Entities.Uom.SaleRefund>().AsNoTracking().Select(row => row.SaleId).Distinct().ToListAsync();
    foreach (var item in saleItems)
    {
        var inForce = completed.SelectMany(row => row.ReturnLines).Where(line => line.SaleItemId == item.Id).Sum(line => line.ReturnedQuantity);
        Check(refundedSaleIds.Contains(item.SaleId) || item.ReturnedQuantity == inForce, $"Sale item {item.Id}: returned {item.ReturnedQuantity} vs exchanges in force {inForce}");
        Check(item.ReturnedQuantity >= 0m && item.ReturnedQuantity <= item.Quantity, $"Sale item {item.Id}: returned {item.ReturnedQuantity} outside 0..{item.Quantity}");
    }
    foreach (var line in exchanges.SelectMany(row => row.ReplacementLines))
    {
        var inForce = completed.SelectMany(row => row.ReturnLines).Where(ret => ret.SourceReplacementLineId == line.Id).Sum(ret => ret.ReturnedQuantity);
        Check(line.ReturnedQuantity == inForce, $"Replacement line {line.Id}: returned {line.ReturnedQuantity} vs re-exchanges in force {inForce}");
    }

    // 5. Every cash settlement reached the drawer once, with the right sign and amount.
    var settlements = await db.ExchangeSettlements.AsNoTracking().Where(row => exchangeIds.Contains(row.ExchangeId)).ToListAsync();
    var movementIds = settlements.Where(row => row.CashSessionMovementId != null).Select(row => row.CashSessionMovementId!.Value).ToList();
    var movements = await db.CashSessionMovements.AsNoTracking().Where(row => movementIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id);
    foreach (var settlement in settlements.Where(row => row.Method == "cash"))
    {
        if (settlement.CashSessionMovementId is not { } id || !movements.TryGetValue(id, out var movement))
        {
            failures.Add($"Cash settlement {settlement.Id} has no drawer movement");
            continue;
        }
        var expected = settlement.Direction == "collect" ? settlement.Amount : -settlement.Amount;
        Check(movement.CashEffect == expected, $"Cash settlement {settlement.Id}: drawer effect {movement.CashEffect} vs {expected}");
    }

    Console.WriteLine($"Reconciled {exchanges.Count} exchanges ({completed.Count} completed, {reversals.Count} reversed), {journals.Count} journals, " +
        $"{inventories.Count} inventory rows, {saleItems.Count} source lines, {settlements.Count} settlements.");
    if (failures.Count == 0)
    {
        Console.WriteLine("RECONCILIATION OK");
        return 0;
    }
    foreach (var failure in failures)
        Console.WriteLine("MISMATCH: " + failure);
    return 1;
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RetailPOS.slnx")))
        directory = directory.Parent;
    return directory?.FullName ?? throw new InvalidOperationException("Repository root (RetailPOS.slnx) not found.");
}

/// <summary>Onboarding audit is not needed for synthetic UAT data.</summary>
sealed class NoAuditService : IAuditService
{
    public Task RecordAsync(AuditEventInput input, CancellationToken ct = default) => Task.CompletedTask;
    public Task RecordFailureAsync(string actionType, string module, string summary, string? entityType, string? entityId, string errorMessage, CancellationToken ct = default) => Task.CompletedTask;
    public AuditEvent BuildPendingEvent(string actionType, string module, string summary, string? primaryEntityType, string? primaryEntityId) => new();
    public void AttachEntity(AuditEvent header, string entityType, string entityId, string operationType,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)>? fields = null, string? metadata = null) { }
    public Task FlushScopeAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Durable delivery is off in UAT; onboarding location events are not needed.</summary>
sealed class NoSourceEvents : IOperationalLocationSourceEventWriter
{
    public Task AppendAsync(string changeType, OperationalLocationSourceEvent source, CancellationToken cancellationToken) => Task.CompletedTask;
}
