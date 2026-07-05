using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using RetailPOS.API.Documents.Pdf;
using RetailPOS.API.Models;
using RetailPOS.API.Middleware;
using RetailPOS.API.Services;
using RetailPOS.API.Authorization;
using RetailPOS.Core.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

// Configure Npgsql to handle DateTime without requiring UTC
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Audit context (scoped per request) — register before DbContext so the interceptor can resolve it
builder.Services.AddScoped<IAuditContext, AuditContext>();
builder.Services.AddScoped<AuditChangeTrackerInterceptor>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IClientIpResolver, ClientIpResolver>();

// ForwardedHeaders — allows ASP.NET Core to see the real client IP when behind a reverse proxy.
// By default only loopback proxies are trusted (safe for local dev).
// PRODUCTION: set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true OR call Configure<ForwardedHeadersOptions>
// and add your proxy IPs to KnownProxies / KnownNetworks, e.g.:
//   options.KnownProxies.Add(IPAddress.Parse("10.0.0.5"));         // Nginx node
//   options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("173.245.48.0"), 20)); // Cloudflare range
// Without explicit configuration, X-Forwarded-For from untrusted sources is ignored by this middleware
// but ClientIpResolver still reads it directly — keep KnownProxies tight in production.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust only loopback by default. Add production proxy IPs here or via environment variables.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    // Allow loopback proxies (safe default for Docker-compose where Nginx → API on same host)
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Loopback, 8));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.IPv6Loopback, 128));
});

// Add services to the container.
builder.Services.AddDbContext<RetailPOSDbContext>((sp, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
           .AddInterceptors(sp.GetRequiredService<AuditChangeTrackerInterceptor>()));

// Configure JWT Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();

// Add Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings!.Secret)),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in PermissionCatalog.All)
    {
        options.AddPolicy(permission, policy =>
        {
            policy.RequireAssertion(context =>
                context.User.Claims.Any(c => c.Type == "permission" && c.Value == "*")
                || context.User.Claims.Any(c => c.Type == "permission" && c.Value == permission));
        });
    }
});

// Register Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBusinessOnboardingService, BusinessOnboardingService>();
builder.Services.AddScoped<IRoleSwitchContext, RoleSwitchContext>();
builder.Services.AddScoped<IRoleSwitchService, RoleSwitchService>();
builder.Services.AddScoped<ITenantAccessService, TenantAccessService>();
builder.Services.AddScoped<IFeatureEntitlementService, FeatureEntitlementService>();

// Register Repositories
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IUserRepository, RetailPOS.Infrastructure.Repositories.UserRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IRoleRepository, RetailPOS.Infrastructure.Repositories.RoleRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.ICategoryRepository, RetailPOS.Infrastructure.Repositories.CategoryRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IProductRepository, RetailPOS.Infrastructure.Repositories.ProductRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IProductVariantRepository, RetailPOS.Infrastructure.Repositories.ProductVariantRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IVariationRepository, RetailPOS.Infrastructure.Repositories.VariationRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IVariationOptionRepository, RetailPOS.Infrastructure.Repositories.VariationOptionRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IInventoryRepository, RetailPOS.Infrastructure.Repositories.InventoryRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IBarcodeTemplateRepository, RetailPOS.Infrastructure.Repositories.BarcodeTemplateRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IBarcodePrintHistoryRepository, RetailPOS.Infrastructure.Repositories.BarcodePrintHistoryRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IOutletRepository, RetailPOS.Infrastructure.Repositories.OutletRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IWarehouseRepository, RetailPOS.Infrastructure.Repositories.WarehouseRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.ISupplierRepository, RetailPOS.Infrastructure.Repositories.SupplierRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IPurchaseOrderRepository, RetailPOS.Infrastructure.Repositories.PurchaseOrderRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IGrnRepository, RetailPOS.Infrastructure.Repositories.GrnRepository>();

// Register User & Role Services
builder.Services.AddScoped<IRoleAssignmentPolicyService, RoleAssignmentPolicyService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();

// Register Master Data Services
builder.Services.AddScoped<IOutletService, OutletService>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IProductIdentifierService, ProductIdentifierService>();
builder.Services.AddScoped<IProductMediaService, ProductMediaService>();
builder.Services.AddScoped<IVariationService, VariationService>();
builder.Services.AddScoped<IProductVariationService, ProductVariationService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IBarcodeService, BarcodeService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<IGrnService, GrnService>();

// Customer Management
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.ICustomerRepository, RetailPOS.Infrastructure.Repositories.CustomerRepository>();
builder.Services.AddScoped<ICustomerService, CustomerService>();

// Sales
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.ISaleRepository, RetailPOS.Infrastructure.Repositories.SaleRepository>();
builder.Services.AddScoped<ISaleService, SaleService>();

// NEW — Hold/Park Sale
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IHeldSaleRepository, RetailPOS.Infrastructure.Repositories.HeldSaleRepository>();
builder.Services.AddScoped<IHeldSaleService, HeldSaleService>();

// NEW — Sale event hook (default in-process publisher; extend by registering ISaleCompletedHandler)
builder.Services.AddScoped<ISaleEventPublisher, LoggingSaleEventPublisher>();

// Stock Movement
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IStockTransferRepository, RetailPOS.Infrastructure.Repositories.StockTransferRepository>();
builder.Services.AddScoped<IStockTransferService, StockTransferService>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IStockRequisitionRepository, RetailPOS.Infrastructure.Repositories.StockRequisitionRepository>();
builder.Services.AddScoped<IStockRequisitionService, StockRequisitionService>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IStockAdjustmentRepository, RetailPOS.Infrastructure.Repositories.StockAdjustmentRepository>();
builder.Services.AddScoped<IStockAdjustmentService, StockAdjustmentService>();
builder.Services.AddScoped<IStockCountService, StockCountService>();

// Stock Ledger  // NEW
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IStockLedgerRepository, RetailPOS.Infrastructure.Repositories.StockLedgerRepository>();
builder.Services.AddScoped<IStockLedgerService, StockLedgerService>();

// Accounting
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IAccountRepository, RetailPOS.Infrastructure.Repositories.AccountRepository>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.ITransactionRepository, RetailPOS.Infrastructure.Repositories.TransactionRepository>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IExpenseRepository, RetailPOS.Infrastructure.Repositories.ExpenseRepository>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IBillRepository, RetailPOS.Infrastructure.Repositories.BillRepository>();
builder.Services.AddScoped<IBillService, BillService>();

// Reports
builder.Services.AddScoped<ISalesReportService, SalesReportService>();
builder.Services.AddScoped<IInventoryReportService, InventoryReportService>();
builder.Services.AddScoped<IUserOutletAccessService, UserOutletAccessService>();

// Pricing Engine
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IPriceRuleRepository, RetailPOS.Infrastructure.Repositories.PriceRuleRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IOutletPriceOverrideRepository, RetailPOS.Infrastructure.Repositories.OutletPriceOverrideRepository>();
builder.Services.AddScoped<IPricingService, PricingService>();

// NEW — POS Performance & Concurrency Layer
// Register IDistributedCache: Redis when a connection string is configured,
// in-memory distributed cache otherwise (safe fallback for local dev / CI).
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration  = redisConnectionString;
        options.InstanceName   = "pos:";
    });
}
else
{
    // Fallback: in-memory distributed cache — functionally identical, not shared across
    // API instances. Sufficient for single-instance dev/CI; use Redis in production.
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSingleton<IPosCacheService, PosCacheService>();
builder.Services.AddScoped<IPosLookupService,  PosLookupService>();

// Settings
builder.Services.AddSingleton<ISettingsService, SettingsService>();

// PDF generation
builder.Services.AddScoped<IDocumentPdfRenderer, QuestDocumentPdfRenderer>();
builder.Services.AddScoped<IPurchaseOrderPdfMapper, PurchaseOrderPdfMapper>();
builder.Services.AddScoped<IPurchaseOrderPdfService, PurchaseOrderPdfService>();
builder.Services.AddScoped<IGrnPdfMapper, GrnPdfMapper>();
builder.Services.AddScoped<IGrnPdfService, GrnPdfService>();
builder.Services.AddScoped<IBarcodeLabelPdfService, BarcodeLabelPdfService>();
builder.Services.AddScoped<IDocumentPdfDispatcher, DocumentPdfDispatcher>();

// Add Controllers
builder.Services.AddControllers();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("auth-register", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("auth-refresh", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<SubscriptionRenewalReminderHostedService>();
}

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Retail POS API",
        Version = "v1",
        Description = "Comprehensive Retail Point of Sale System API"
    });

    // Prevent schema ID clashes when different namespaces contain same DTO class names.
    options.CustomSchemaIds(GenerateSwaggerSchemaId);
    
    // Add JWT Authentication to Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

static string GenerateSwaggerSchemaId(Type type)
{
    if (type.IsGenericType)
    {
        var genericTypeName = type.GetGenericTypeDefinition().Name;
        var tickIndex = genericTypeName.IndexOf('`');
        if (tickIndex > 0)
        {
            genericTypeName = genericTypeName[..tickIndex];
        }

        var genericArguments = string.Join("And", type.GetGenericArguments().Select(GenerateSwaggerSchemaId));
        return $"{genericTypeName}Of{genericArguments}";
    }

    var fullName = type.FullName ?? type.Name;
    return fullName
        .Replace('.', '_')
        .Replace('+', '_')
        .Replace("[]", "Array");
}

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    // Seed database on startup
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<RetailPOSDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();

        // Ensure database is created and migrations are applied
        await context.Database.MigrateAsync();

        // Idempotent DDL for the product_variation_selected_options table.
        // Executed every startup so new deployments automatically get the table
        // without requiring a full EF Core migration workflow.
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS product_variation_selected_options (
                id          BIGSERIAL    PRIMARY KEY,
                product_id  BIGINT       NOT NULL REFERENCES products(id)          ON DELETE CASCADE,
                variation_id BIGINT      NOT NULL REFERENCES variations(id)        ON DELETE CASCADE,
                option_id   BIGINT       NOT NULL REFERENCES variation_options(id) ON DELETE CASCADE,
                created_at  TIMESTAMP    NOT NULL DEFAULT NOW(),
                UNIQUE (product_id, variation_id, option_id)
            )");

        logger.LogInformation("Checking if database needs seeding...");

        // Seed default roles, users, and all module sample data
        await RetailPOS.Infrastructure.Data.DbSeeder.SeedAsync(context);

        logger.LogInformation("Database seeding completed successfully");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Retail POS API v1");
        options.RoutePrefix = "swagger";
    });
}

// Disable HTTPS redirection in development
// app.UseHttpsRedirection()

// Must be first so RemoteIpAddress is rewritten before any middleware reads it.
app.UseForwardedHeaders();

// Add global exception handler middleware
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseCors("AllowAngularApp");
app.UseRateLimiter();

// Serve uploaded product images from wwwroot/uploads with long-lived cache headers.
// Filenames embed the image id so URLs are immutable; safe to cache aggressively.
{
    var uploadsRoot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
    Directory.CreateDirectory(uploadsRoot);
    app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsRoot),
        RequestPath = "/uploads",
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=2592000,immutable";
        },
    });
}

app.UseAuthentication();
app.UseAuthorization();

// Audit context — must be after authentication so user claims are available
app.UseMiddleware<AuditContextMiddleware>();

app.MapControllers();

app.Run();

public partial class Program { }
