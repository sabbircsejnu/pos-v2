using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RetailPOS.API.Models;
using RetailPOS.API.Middleware;
using RetailPOS.API.Services;
using RetailPOS.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Configure Npgsql to handle DateTime without requiring UTC
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Add services to the container.
builder.Services.AddDbContext<RetailPOSDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

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

builder.Services.AddAuthorization();

// Register Services
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Register Repositories
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IUserRepository, RetailPOS.Infrastructure.Repositories.UserRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IRoleRepository, RetailPOS.Infrastructure.Repositories.RoleRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.ICategoryRepository, RetailPOS.Infrastructure.Repositories.CategoryRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IProductRepository, RetailPOS.Infrastructure.Repositories.ProductRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IProductVariantRepository, RetailPOS.Infrastructure.Repositories.ProductVariantRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IVariationRepository, RetailPOS.Infrastructure.Repositories.VariationRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IVariationOptionRepository, RetailPOS.Infrastructure.Repositories.VariationOptionRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IInventoryRepository, RetailPOS.Infrastructure.Repositories.InventoryRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IOutletRepository, RetailPOS.Infrastructure.Repositories.OutletRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IWarehouseRepository, RetailPOS.Infrastructure.Repositories.WarehouseRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.ISupplierRepository, RetailPOS.Infrastructure.Repositories.SupplierRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IPurchaseOrderRepository, RetailPOS.Infrastructure.Repositories.PurchaseOrderRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IGrnRepository, RetailPOS.Infrastructure.Repositories.GrnRepository>();

// Register User & Role Services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();

// Register Master Data Services
builder.Services.AddScoped<IOutletService, OutletService>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IVariationService, VariationService>();
builder.Services.AddScoped<IProductVariationService, ProductVariationService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<IGrnService, GrnService>();

// Customer Management
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.ICustomerRepository, RetailPOS.Infrastructure.Repositories.CustomerRepository>();
builder.Services.AddScoped<ICustomerService, CustomerService>();

// Sales
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.ISaleRepository, RetailPOS.Infrastructure.Repositories.SaleRepository>();
builder.Services.AddScoped<ISaleService, SaleService>();

// Stock Movement
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IStockTransferRepository, RetailPOS.Infrastructure.Repositories.StockTransferRepository>();
builder.Services.AddScoped<IStockTransferService, StockTransferService>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IStockAdjustmentRepository, RetailPOS.Infrastructure.Repositories.StockAdjustmentRepository>();
builder.Services.AddScoped<IStockAdjustmentService, StockAdjustmentService>();

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

// Settings
builder.Services.AddSingleton<ISettingsService, SettingsService>();

// Add Controllers
builder.Services.AddControllers();

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

var app = builder.Build();

// Seed database on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<RetailPOSDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        // Ensure database is created and migrations are applied
        await context.Database.MigrateAsync();
        
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
// app.UseHttpsRedirection();

// Add global exception handler middleware
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseCors("AllowAngularApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
