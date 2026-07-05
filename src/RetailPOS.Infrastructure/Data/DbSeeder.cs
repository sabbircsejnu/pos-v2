using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using BCrypt.Net;

namespace RetailPOS.Infrastructure.Data;

/// <summary>
/// Database seeder for initial data setup.
/// Seeds default roles, users, and sample data for all modules.
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// Seeds the database with all initial and sample data.
    /// Only runs if no roles exist in the database.
    /// </summary>
    public static async Task SeedAsync(RetailPOSDbContext context)
    {
        // Always ensure the BusinessOwner role + seed user exist (idempotent).
        // This runs even on already-seeded databases so older deployments pick up new roles.
        await EnsureBusinessOwnerAsync(context);
        await EnsureDefaultPosTerminalsAsync(context);

        if (await context.Roles.AnyAsync())
        {
            return; // Database already seeded
        }

        // 1. Seed Roles
        var roles = GetDefaultRoles();
        await context.Roles.AddRangeAsync(roles);
        await context.SaveChangesAsync();

        var superAdminRole = await context.Roles.FirstAsync(r => r.Name == "Super Admin");
        var businessOwnerRole = await context.Roles.FirstAsync(r => r.Name == "BusinessOwner");
        var adminRole = await context.Roles.FirstAsync(r => r.Name == "Admin");
        var managerRole = await context.Roles.FirstAsync(r => r.Name == "Manager");
        var cashierRole = await context.Roles.FirstAsync(r => r.Name == "Cashier");
        var stockManagerRole = await context.Roles.FirstAsync(r => r.Name == "Stock Manager");

        // 2. Seed Outlets (no managers yet)
        var outlets = new List<Outlet>
        {
            new Outlet { Name = "Main Store", Address = "123 Main Street, New York, NY 10001", ContactNumber = "+1-555-0100", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Outlet { Name = "Downtown Branch", Address = "456 Downtown Ave, New York, NY 10002", ContactNumber = "+1-555-0200", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        await context.Outlets.AddRangeAsync(outlets);
        await context.SaveChangesAsync();

        var mainOutlet = outlets[0];
        var branchOutlet = outlets[1];

        var terminals = new List<PosTerminal>
        {
            new PosTerminal
            {
                OutletId = mainOutlet.Id,
                Name = "Main Counter 1",
                Code = "MAIN-01",
                IsActive = true,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new PosTerminal
            {
                OutletId = branchOutlet.Id,
                Name = "Branch Counter 1",
                Code = "BR-01",
                IsActive = true,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };
        await context.PosTerminals.AddRangeAsync(terminals);
        await context.SaveChangesAsync();

        var mainTerminal = terminals[0];
        var branchTerminal = terminals[1];

        // 3. Seed Warehouses (no managers yet)
        var warehouses = new List<Warehouse>
        {
            new Warehouse { Name = "Central Warehouse", Address = "789 Warehouse Blvd, Brooklyn, NY 11201", Capacity = 5000, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Warehouse { Name = "North Warehouse", Address = "321 North Industrial Rd, Queens, NY 11101", Capacity = 3000, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        await context.Warehouses.AddRangeAsync(warehouses);
        await context.SaveChangesAsync();

        var centralWarehouse = warehouses[0];
        var northWarehouse = warehouses[1];

        // 4. Seed Users
        var users = new List<User>
        {
            // Work factor 12 matches the production default; acceptable here since seeding runs only once.
            new User { Name = "Super Admin", Email = "suparadmin@sabbir.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123", workFactor: 12), RoleId = superAdminRole.Id, OutletId = null, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Name = "John Admin", Email = "john.admin@retailpos.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123", workFactor: 12), RoleId = adminRole.Id, OutletId = mainOutlet.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Name = "Sarah Manager", Email = "sarah.manager@retailpos.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Manager@123", workFactor: 12), RoleId = managerRole.Id, OutletId = mainOutlet.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Name = "Mike Cashier", Email = "mike.cashier@retailpos.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Cashier@123", workFactor: 12), RoleId = cashierRole.Id, OutletId = mainOutlet.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Name = "Lisa Branch Manager", Email = "lisa.manager@retailpos.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Manager@123", workFactor: 12), RoleId = managerRole.Id, OutletId = branchOutlet.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Name = "Tom Stock Manager", Email = "tom.stock@retailpos.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Stock@123", workFactor: 12), RoleId = stockManagerRole.Id, OutletId = null, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Name = "Business Owner", Email = "owner@retailpos.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123", workFactor: 12), RoleId = businessOwnerRole.Id, OutletId = null, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        await context.Users.AddRangeAsync(users);
        await context.SaveChangesAsync();

        var superAdmin = users[0];
        var sarahManager = users[2];
        var lisaManager = users[4];
        var tomStock = users[5];

        // 5. Update Outlet and Warehouse managers
        mainOutlet.ManagerId = sarahManager.Id;
        branchOutlet.ManagerId = lisaManager.Id;
        centralWarehouse.ManagerId = tomStock.Id;
        northWarehouse.ManagerId = tomStock.Id;
        await context.SaveChangesAsync();

        // 6. Seed Categories
        var rootCategories = new List<Category>
        {
            new Category { Name = "Electronics", Description = "Electronic devices and accessories", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Category { Name = "Clothing", Description = "Apparel and fashion items", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Category { Name = "Food & Beverages", Description = "Food products and drinks", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Category { Name = "Home & Garden", Description = "Home essentials and gardening supplies", DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Category { Name = "Sports & Outdoors", Description = "Sporting goods and outdoor equipment", DisplayOrder = 5, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        await context.Categories.AddRangeAsync(rootCategories);
        await context.SaveChangesAsync();

        var electronicsCategory = rootCategories[0];
        var clothingCategory = rootCategories[1];
        var foodCategory = rootCategories[2];

        var subCategories = new List<Category>
        {
            new Category { Name = "Laptops", Description = "Laptop computers", ParentCategoryId = electronicsCategory.Id, DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Category { Name = "Mobile Phones", Description = "Smartphones and mobile devices", ParentCategoryId = electronicsCategory.Id, DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Category { Name = "Accessories", Description = "Electronic accessories", ParentCategoryId = electronicsCategory.Id, DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Category { Name = "Men's Wear", Description = "Men's clothing", ParentCategoryId = clothingCategory.Id, DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Category { Name = "Women's Wear", Description = "Women's clothing", ParentCategoryId = clothingCategory.Id, DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Category { Name = "Coffee & Tea", Description = "Coffee beans, tea, and hot beverages", ParentCategoryId = foodCategory.Id, DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        await context.Categories.AddRangeAsync(subCategories);
        await context.SaveChangesAsync();

        var laptopCategory = subCategories[0];
        var phonesCategory = subCategories[1];
        var accessoriesCategory = subCategories[2];
        var mensWearCategory = subCategories[3];
        var womensWearCategory = subCategories[4];
        var coffeeCategory = subCategories[5];

        // 7. Seed Suppliers
        var suppliers = new List<Supplier>
        {
            new Supplier { Name = "TechSupply Co.", Contact = "contact@techsupply.com", Address = "500 Tech Park, San Jose, CA 95101", CreditLimit = 50000, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Supplier { Name = "Fashion World Distributors", Contact = "orders@fashionworld.com", Address = "200 Fashion District, Los Angeles, CA 90015", CreditLimit = 30000, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Supplier { Name = "Global Food Imports", Contact = "sales@globalfood.com", Address = "100 Harbor View, Miami, FL 33101", CreditLimit = 20000, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        await context.Suppliers.AddRangeAsync(suppliers);
        await context.SaveChangesAsync();

        var techSupplier = suppliers[0];
        var fashionSupplier = suppliers[1];
        var foodSupplier = suppliers[2];

        // 8. Seed Variations with Options
        var colorVariation = new Variation { Name = "Color", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var sizeVariation = new Variation { Name = "Size", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var storageVariation = new Variation { Name = "Storage", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

        await context.Variations.AddRangeAsync(colorVariation, sizeVariation, storageVariation);
        await context.SaveChangesAsync();

        var colorOptions = new List<VariationOption>
        {
            new VariationOption { VariationId = colorVariation.Id, Name = "Black", PriceAdjustment = 0, DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new VariationOption { VariationId = colorVariation.Id, Name = "White", PriceAdjustment = 0, DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new VariationOption { VariationId = colorVariation.Id, Name = "Blue", PriceAdjustment = 0, DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new VariationOption { VariationId = colorVariation.Id, Name = "Red", PriceAdjustment = 0, DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        var sizeOptions = new List<VariationOption>
        {
            new VariationOption { VariationId = sizeVariation.Id, Name = "XS", PriceAdjustment = 0, DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new VariationOption { VariationId = sizeVariation.Id, Name = "S", PriceAdjustment = 0, DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new VariationOption { VariationId = sizeVariation.Id, Name = "M", PriceAdjustment = 0, DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new VariationOption { VariationId = sizeVariation.Id, Name = "L", PriceAdjustment = 5, DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new VariationOption { VariationId = sizeVariation.Id, Name = "XL", PriceAdjustment = 10, DisplayOrder = 5, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        var storageOptions = new List<VariationOption>
        {
            new VariationOption { VariationId = storageVariation.Id, Name = "128GB", PriceAdjustment = 0, DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new VariationOption { VariationId = storageVariation.Id, Name = "256GB", PriceAdjustment = 50, DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new VariationOption { VariationId = storageVariation.Id, Name = "512GB", PriceAdjustment = 150, DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };

        await context.VariationOptions.AddRangeAsync(colorOptions);
        await context.VariationOptions.AddRangeAsync(sizeOptions);
        await context.VariationOptions.AddRangeAsync(storageOptions);
        await context.SaveChangesAsync();

        var black = colorOptions[0];
        var white = colorOptions[1];
        var blue = colorOptions[2];
        var red = colorOptions[3];
        var xs = sizeOptions[0];
        var s = sizeOptions[1];
        var m = sizeOptions[2];
        var l = sizeOptions[3];
        var xl = sizeOptions[4];
        var storage128 = storageOptions[0];
        var storage256 = storageOptions[1];
        var storage512 = storageOptions[2];

        // 9. Seed Products
        // Product 1: Laptop (no variants)
        var laptopProduct = new Product { Name = "ProBook Laptop 15", Description = "High-performance 15-inch laptop", Sku = "LPTP-001", Barcode = "8901234567890", CategoryId = laptopCategory.Id, BasePrice = 999.99m, CostPrice = 750.00m, TaxRate = 8.5m, HasVariants = false, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        // Product 2: Smartphone (with storage variants)
        var phoneProduct = new Product { Name = "SmartPhone X12", Description = "Latest flagship smartphone", Sku = "PHN-X12", Barcode = "8901234567891", CategoryId = phonesCategory.Id, BasePrice = 699.99m, CostPrice = 500.00m, TaxRate = 8.5m, HasVariants = true, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        // Product 3: USB Cable (no variants)
        var cableProduct = new Product { Name = "USB-C Cable 2m", Description = "High-speed USB-C cable 2 meters", Sku = "ACC-USB-C-2M", Barcode = "8901234567892", CategoryId = accessoriesCategory.Id, BasePrice = 19.99m, CostPrice = 5.00m, TaxRate = 8.5m, HasVariants = false, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        // Product 4: Men's T-Shirt (with color and size variants)
        var tshirtProduct = new Product { Name = "Classic Crew T-Shirt", Description = "100% cotton crew-neck t-shirt", Sku = "CLT-MENS-TS", Barcode = "8901234567893", CategoryId = mensWearCategory.Id, BasePrice = 29.99m, CostPrice = 10.00m, TaxRate = 0m, HasVariants = true, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        // Product 5: Women's Dress (with color and size variants)
        var dressProduct = new Product { Name = "Summer Floral Dress", Description = "Light and elegant summer dress", Sku = "CLT-WMN-DRS", Barcode = "8901234567894", CategoryId = womensWearCategory.Id, BasePrice = 49.99m, CostPrice = 18.00m, TaxRate = 0m, HasVariants = true, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        // Product 6: Coffee Beans (no variants)
        var coffeeProduct = new Product { Name = "Arabica Coffee Beans 1kg", Description = "Premium single-origin Arabica beans", Sku = "FOOD-COF-1KG", Barcode = "8901234567895", CategoryId = coffeeCategory.Id, BasePrice = 24.99m, CostPrice = 12.00m, TaxRate = 5m, HasVariants = false, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        // Product 7: Wireless Earbuds (with color variants)
        var earbudsProduct = new Product { Name = "TrueWireless Earbuds", Description = "Noise-cancelling wireless earbuds", Sku = "ACC-EAR-TWS", Barcode = "8901234567896", CategoryId = accessoriesCategory.Id, BasePrice = 89.99m, CostPrice = 35.00m, TaxRate = 8.5m, HasVariants = true, Status = ProductStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

        await context.Products.AddRangeAsync(laptopProduct, phoneProduct, cableProduct, tshirtProduct, dressProduct, coffeeProduct, earbudsProduct);
        await context.SaveChangesAsync();

        // 10. Seed ProductVariations (link products to applicable variations)
        var productVariations = new List<ProductVariation>
        {
            // Phone - Storage variation
            new ProductVariation { ProductId = phoneProduct.Id, VariationId = storageVariation.Id, IsRequired = true, CreatedAt = DateTime.UtcNow },
            new ProductVariation { ProductId = phoneProduct.Id, VariationId = colorVariation.Id, IsRequired = true, CreatedAt = DateTime.UtcNow },
            // T-Shirt - Color and Size
            new ProductVariation { ProductId = tshirtProduct.Id, VariationId = colorVariation.Id, IsRequired = true, CreatedAt = DateTime.UtcNow },
            new ProductVariation { ProductId = tshirtProduct.Id, VariationId = sizeVariation.Id, IsRequired = true, CreatedAt = DateTime.UtcNow },
            // Dress - Color and Size
            new ProductVariation { ProductId = dressProduct.Id, VariationId = colorVariation.Id, IsRequired = true, CreatedAt = DateTime.UtcNow },
            new ProductVariation { ProductId = dressProduct.Id, VariationId = sizeVariation.Id, IsRequired = true, CreatedAt = DateTime.UtcNow },
            // Earbuds - Color
            new ProductVariation { ProductId = earbudsProduct.Id, VariationId = colorVariation.Id, IsRequired = true, CreatedAt = DateTime.UtcNow }
        };
        await context.ProductVariations.AddRangeAsync(productVariations);
        await context.SaveChangesAsync();

        // 11. Seed ProductVariants
        // Laptop - single default variant
        var laptopVariant = new ProductVariant { ProductId = laptopProduct.Id, Name = "ProBook Laptop 15 - Default", Sku = "LPTP-001-DEF", Attributes = "{}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        // Phone variants: Black/128GB, Black/256GB, White/128GB, White/256GB
        var phoneVariants = new List<ProductVariant>
        {
            new ProductVariant { ProductId = phoneProduct.Id, Name = "SmartPhone X12 - Black 128GB", Sku = "PHN-X12-BLK-128", Barcode = "8901234568001", Attributes = "{\"Color\":\"Black\",\"Storage\":\"128GB\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = phoneProduct.Id, Name = "SmartPhone X12 - Black 256GB", Sku = "PHN-X12-BLK-256", Barcode = "8901234568002", Attributes = "{\"Color\":\"Black\",\"Storage\":\"256GB\"}", PriceAdjustment = 50, CostAdjustment = 30, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = phoneProduct.Id, Name = "SmartPhone X12 - White 128GB", Sku = "PHN-X12-WHT-128", Barcode = "8901234568003", Attributes = "{\"Color\":\"White\",\"Storage\":\"128GB\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = phoneProduct.Id, Name = "SmartPhone X12 - White 256GB", Sku = "PHN-X12-WHT-256", Barcode = "8901234568004", Attributes = "{\"Color\":\"White\",\"Storage\":\"256GB\"}", PriceAdjustment = 50, CostAdjustment = 30, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        // USB Cable - single default variant
        var cableVariant = new ProductVariant { ProductId = cableProduct.Id, Name = "USB-C Cable 2m - Default", Sku = "ACC-USB-C-2M-DEF", Attributes = "{}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        // T-Shirt variants: Black/S, Black/M, Black/L, Blue/S, Blue/M, Blue/L
        var tshirtVariants = new List<ProductVariant>
        {
            new ProductVariant { ProductId = tshirtProduct.Id, Name = "Classic Crew T-Shirt - Black S", Sku = "CLT-TS-BLK-S", Barcode = "8901234568101", Attributes = "{\"Color\":\"Black\",\"Size\":\"S\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = tshirtProduct.Id, Name = "Classic Crew T-Shirt - Black M", Sku = "CLT-TS-BLK-M", Barcode = "8901234568102", Attributes = "{\"Color\":\"Black\",\"Size\":\"M\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = tshirtProduct.Id, Name = "Classic Crew T-Shirt - Black L", Sku = "CLT-TS-BLK-L", Barcode = "8901234568103", Attributes = "{\"Color\":\"Black\",\"Size\":\"L\"}", PriceAdjustment = 5, CostAdjustment = 2, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = tshirtProduct.Id, Name = "Classic Crew T-Shirt - Blue S", Sku = "CLT-TS-BLU-S", Barcode = "8901234568104", Attributes = "{\"Color\":\"Blue\",\"Size\":\"S\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = tshirtProduct.Id, Name = "Classic Crew T-Shirt - Blue M", Sku = "CLT-TS-BLU-M", Barcode = "8901234568105", Attributes = "{\"Color\":\"Blue\",\"Size\":\"M\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        // Dress variants: White/S, White/M, Blue/S, Blue/M
        var dressVariants = new List<ProductVariant>
        {
            new ProductVariant { ProductId = dressProduct.Id, Name = "Summer Floral Dress - White S", Sku = "CLT-DRS-WHT-S", Barcode = "8901234568201", Attributes = "{\"Color\":\"White\",\"Size\":\"S\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = dressProduct.Id, Name = "Summer Floral Dress - White M", Sku = "CLT-DRS-WHT-M", Barcode = "8901234568202", Attributes = "{\"Color\":\"White\",\"Size\":\"M\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = dressProduct.Id, Name = "Summer Floral Dress - Blue S", Sku = "CLT-DRS-BLU-S", Barcode = "8901234568203", Attributes = "{\"Color\":\"Blue\",\"Size\":\"S\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = dressProduct.Id, Name = "Summer Floral Dress - Blue M", Sku = "CLT-DRS-BLU-M", Barcode = "8901234568204", Attributes = "{\"Color\":\"Blue\",\"Size\":\"M\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        // Coffee - single default variant
        var coffeeVariant = new ProductVariant { ProductId = coffeeProduct.Id, Name = "Arabica Coffee Beans 1kg - Default", Sku = "FOOD-COF-1KG-DEF", Attributes = "{}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        // Earbuds - Black and White variants
        var earbudsVariants = new List<ProductVariant>
        {
            new ProductVariant { ProductId = earbudsProduct.Id, Name = "TrueWireless Earbuds - Black", Sku = "ACC-EAR-TWS-BLK", Barcode = "8901234568301", Attributes = "{\"Color\":\"Black\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new ProductVariant { ProductId = earbudsProduct.Id, Name = "TrueWireless Earbuds - White", Sku = "ACC-EAR-TWS-WHT", Barcode = "8901234568302", Attributes = "{\"Color\":\"White\"}", PriceAdjustment = 0, CostAdjustment = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };

        await context.ProductVariants.AddAsync(laptopVariant);
        await context.ProductVariants.AddRangeAsync(phoneVariants);
        await context.ProductVariants.AddAsync(cableVariant);
        await context.ProductVariants.AddRangeAsync(tshirtVariants);
        await context.ProductVariants.AddRangeAsync(dressVariants);
        await context.ProductVariants.AddAsync(coffeeVariant);
        await context.ProductVariants.AddRangeAsync(earbudsVariants);
        await context.SaveChangesAsync();

        // 12. Seed ProductVariantOptions (link variants to variation options)
        var variantOptions = new List<ProductVariantOption>
        {
            // Phone variants - Black/128GB
            new ProductVariantOption { VariantId = phoneVariants[0].Id, OptionId = black.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = phoneVariants[0].Id, OptionId = storage128.Id, CreatedAt = DateTime.UtcNow },
            // Phone - Black/256GB
            new ProductVariantOption { VariantId = phoneVariants[1].Id, OptionId = black.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = phoneVariants[1].Id, OptionId = storage256.Id, CreatedAt = DateTime.UtcNow },
            // Phone - White/128GB
            new ProductVariantOption { VariantId = phoneVariants[2].Id, OptionId = white.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = phoneVariants[2].Id, OptionId = storage128.Id, CreatedAt = DateTime.UtcNow },
            // Phone - White/256GB
            new ProductVariantOption { VariantId = phoneVariants[3].Id, OptionId = white.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = phoneVariants[3].Id, OptionId = storage256.Id, CreatedAt = DateTime.UtcNow },
            // T-Shirt variants
            new ProductVariantOption { VariantId = tshirtVariants[0].Id, OptionId = black.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = tshirtVariants[0].Id, OptionId = s.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = tshirtVariants[1].Id, OptionId = black.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = tshirtVariants[1].Id, OptionId = m.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = tshirtVariants[2].Id, OptionId = black.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = tshirtVariants[2].Id, OptionId = l.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = tshirtVariants[3].Id, OptionId = blue.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = tshirtVariants[3].Id, OptionId = s.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = tshirtVariants[4].Id, OptionId = blue.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = tshirtVariants[4].Id, OptionId = m.Id, CreatedAt = DateTime.UtcNow },
            // Dress variants
            new ProductVariantOption { VariantId = dressVariants[0].Id, OptionId = white.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = dressVariants[0].Id, OptionId = s.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = dressVariants[1].Id, OptionId = white.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = dressVariants[1].Id, OptionId = m.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = dressVariants[2].Id, OptionId = blue.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = dressVariants[2].Id, OptionId = s.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = dressVariants[3].Id, OptionId = blue.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = dressVariants[3].Id, OptionId = m.Id, CreatedAt = DateTime.UtcNow },
            // Earbuds variants
            new ProductVariantOption { VariantId = earbudsVariants[0].Id, OptionId = black.Id, CreatedAt = DateTime.UtcNow },
            new ProductVariantOption { VariantId = earbudsVariants[1].Id, OptionId = white.Id, CreatedAt = DateTime.UtcNow }
        };
        await context.ProductVariantOptions.AddRangeAsync(variantOptions);
        await context.SaveChangesAsync();

        // 13. Seed Inventory (stock at outlets and central warehouse)
        var inventoryRecords = new List<Inventory>();

        // Laptop at Main Outlet and Central Warehouse
        inventoryRecords.Add(new Inventory { VariantId = laptopVariant.Id, LocationId = mainOutlet.Id, LocationType = "outlet", Quantity = 15, LowStockThreshold = 3 });
        inventoryRecords.Add(new Inventory { VariantId = laptopVariant.Id, LocationId = centralWarehouse.Id, LocationType = "warehouse", Quantity = 50, LowStockThreshold = 10 });

        // Phone variants at Main Outlet and Warehouse
        foreach (var pv in phoneVariants)
        {
            inventoryRecords.Add(new Inventory { VariantId = pv.Id, LocationId = mainOutlet.Id, LocationType = "outlet", Quantity = 20, LowStockThreshold = 5 });
            inventoryRecords.Add(new Inventory { VariantId = pv.Id, LocationId = centralWarehouse.Id, LocationType = "warehouse", Quantity = 80, LowStockThreshold = 20 });
        }

        // USB Cable at both outlets and warehouse
        inventoryRecords.Add(new Inventory { VariantId = cableVariant.Id, LocationId = mainOutlet.Id, LocationType = "outlet", Quantity = 100, LowStockThreshold = 20 });
        inventoryRecords.Add(new Inventory { VariantId = cableVariant.Id, LocationId = branchOutlet.Id, LocationType = "outlet", Quantity = 50, LowStockThreshold = 10 });
        inventoryRecords.Add(new Inventory { VariantId = cableVariant.Id, LocationId = centralWarehouse.Id, LocationType = "warehouse", Quantity = 500, LowStockThreshold = 100 });

        // T-Shirt variants at Main Outlet
        foreach (var tv in tshirtVariants)
        {
            inventoryRecords.Add(new Inventory { VariantId = tv.Id, LocationId = mainOutlet.Id, LocationType = "outlet", Quantity = 30, LowStockThreshold = 5 });
            inventoryRecords.Add(new Inventory { VariantId = tv.Id, LocationId = branchOutlet.Id, LocationType = "outlet", Quantity = 15, LowStockThreshold = 3 });
            inventoryRecords.Add(new Inventory { VariantId = tv.Id, LocationId = centralWarehouse.Id, LocationType = "warehouse", Quantity = 200, LowStockThreshold = 50 });
        }

        // Dress variants at both outlets
        foreach (var dv in dressVariants)
        {
            inventoryRecords.Add(new Inventory { VariantId = dv.Id, LocationId = mainOutlet.Id, LocationType = "outlet", Quantity = 20, LowStockThreshold = 3 });
            inventoryRecords.Add(new Inventory { VariantId = dv.Id, LocationId = branchOutlet.Id, LocationType = "outlet", Quantity = 10, LowStockThreshold = 2 });
        }

        // Coffee at Main Outlet and Warehouse
        inventoryRecords.Add(new Inventory { VariantId = coffeeVariant.Id, LocationId = mainOutlet.Id, LocationType = "outlet", Quantity = 60, LowStockThreshold = 10 });
        inventoryRecords.Add(new Inventory { VariantId = coffeeVariant.Id, LocationId = centralWarehouse.Id, LocationType = "warehouse", Quantity = 300, LowStockThreshold = 50 });

        // Earbuds at both outlets and warehouse
        foreach (var ev in earbudsVariants)
        {
            inventoryRecords.Add(new Inventory { VariantId = ev.Id, LocationId = mainOutlet.Id, LocationType = "outlet", Quantity = 25, LowStockThreshold = 5 });
            inventoryRecords.Add(new Inventory { VariantId = ev.Id, LocationId = branchOutlet.Id, LocationType = "outlet", Quantity = 10, LowStockThreshold = 3 });
            inventoryRecords.Add(new Inventory { VariantId = ev.Id, LocationId = centralWarehouse.Id, LocationType = "warehouse", Quantity = 100, LowStockThreshold = 20 });
        }

        await context.Inventories.AddRangeAsync(inventoryRecords);
        await context.SaveChangesAsync();

        // 14. Seed Customers
        var customers = new List<Customer>
        {
            new Customer { Name = Customer.WalkInCustomerName, CustomerCode = Customer.WalkInCustomerCode, IsSystem = true, IsActive = true, LoyaltyPoints = 0, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "Alice Johnson", IsSystem = false, IsActive = true, Phone = "+1-555-1001", Email = "alice.johnson@example.com", LoyaltyPoints = 250, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "Bob Williams", IsSystem = false, IsActive = true, Phone = "+1-555-1002", Email = "bob.williams@example.com", LoyaltyPoints = 100, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "Carol Davis", IsSystem = false, IsActive = true, Phone = "+1-555-1003", Email = "carol.davis@example.com", LoyaltyPoints = 500, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "David Brown", IsSystem = false, IsActive = true, Phone = "+1-555-1004", Email = "david.brown@example.com", LoyaltyPoints = 75, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "Emma Wilson", IsSystem = false, IsActive = true, Phone = "+1-555-1005", Email = "emma.wilson@example.com", LoyaltyPoints = 320, CreatedAt = DateTime.UtcNow }
        };
        await context.Customers.AddRangeAsync(customers);
        await context.SaveChangesAsync();

        // 15. Seed Accounts
        var accounts = new List<Account>
        {
            new Account { Name = "Main Store Cash Register", Type = "asset", Balance = 5000 },
            new Account { Name = "Business Bank Account", Type = "asset", Balance = 150000 },
            new Account { Name = "Accounts Payable", Type = "liability", Balance = 12000 },
            new Account { Name = "Accounts Receivable", Type = "asset", Balance = 8500 },
            new Account { Name = "General Expenses", Type = "expense", Balance = 0 },
            new Account { Name = "Sales Revenue", Type = "revenue", Balance = 0 }
        };
        await context.Accounts.AddRangeAsync(accounts);
        await context.SaveChangesAsync();

        var cashAccount = accounts[0];
        var bankAccount = accounts[1];

        // 16. Seed Purchase Orders
        var now = DateTime.UtcNow;
        var po1 = new PurchaseOrder
        {
            SupplierId = techSupplier.Id, WarehouseId = centralWarehouse.Id,
            OrderDate = now.AddDays(-30), ExpectedDelivery = now.AddDays(-20),
            TotalAmount = 18500m, Status = "received", CreatedBy = superAdmin.Id,
            CreatedAt = now.AddDays(-30), UpdatedAt = now.AddDays(-20)
        };
        var po2 = new PurchaseOrder
        {
            SupplierId = fashionSupplier.Id, WarehouseId = centralWarehouse.Id,
            OrderDate = now.AddDays(-15), ExpectedDelivery = now.AddDays(-5),
            TotalAmount = 5500m, Status = "received", CreatedBy = tomStock.Id,
            CreatedAt = now.AddDays(-15), UpdatedAt = now.AddDays(-5)
        };
        var po3 = new PurchaseOrder
        {
            SupplierId = techSupplier.Id, WarehouseId = northWarehouse.Id,
            OrderDate = now.AddDays(-5), ExpectedDelivery = now.AddDays(5),
            TotalAmount = 9999m, Status = "pending", CreatedBy = tomStock.Id,
            CreatedAt = now.AddDays(-5), UpdatedAt = now.AddDays(-5)
        };
        await context.PurchaseOrders.AddRangeAsync(po1, po2, po3);
        await context.SaveChangesAsync();

        var po1Items = new List<PurchaseOrderItem>
        {
            new PurchaseOrderItem { PoId = po1.Id, VariantId = laptopVariant.Id, Quantity = 20, UnitPrice = 750m },
            new PurchaseOrderItem { PoId = po1.Id, VariantId = phoneVariants[0].Id, Quantity = 30, UnitPrice = 500m }
        };
        var po2Items = new List<PurchaseOrderItem>
        {
            new PurchaseOrderItem { PoId = po2.Id, VariantId = tshirtVariants[0].Id, Quantity = 100, UnitPrice = 10m },
            new PurchaseOrderItem { PoId = po2.Id, VariantId = tshirtVariants[1].Id, Quantity = 100, UnitPrice = 10m },
            new PurchaseOrderItem { PoId = po2.Id, VariantId = dressVariants[0].Id, Quantity = 50, UnitPrice = 18m }
        };
        var po3Items = new List<PurchaseOrderItem>
        {
            new PurchaseOrderItem { PoId = po3.Id, VariantId = earbudsVariants[0].Id, Quantity = 150, UnitPrice = 35m },
            new PurchaseOrderItem { PoId = po3.Id, VariantId = earbudsVariants[1].Id, Quantity = 150, UnitPrice = 35m }
        };
        await context.PurchaseOrderItems.AddRangeAsync(po1Items);
        await context.PurchaseOrderItems.AddRangeAsync(po2Items);
        await context.PurchaseOrderItems.AddRangeAsync(po3Items);
        await context.SaveChangesAsync();

        // 17. Seed GRNs (Goods Received Notes) for received POs
        var grn1 = new Grn { PoId = po1.Id, ReceivedDate = now.AddDays(-20), Status = "full", CreatedBy = tomStock.Id, CreatedAt = now.AddDays(-20) };
        var grn2 = new Grn { PoId = po2.Id, ReceivedDate = now.AddDays(-5), Status = "full", CreatedBy = tomStock.Id, CreatedAt = now.AddDays(-5) };
        await context.Grns.AddRangeAsync(grn1, grn2);
        await context.SaveChangesAsync();

        var grn1Items = new List<GrnItem>
        {
            new GrnItem { GrnId = grn1.Id, PoItemId = po1Items[0].Id, ReceivedQty = 20 },
            new GrnItem { GrnId = grn1.Id, PoItemId = po1Items[1].Id, ReceivedQty = 30 }
        };
        var grn2Items = new List<GrnItem>
        {
            new GrnItem { GrnId = grn2.Id, PoItemId = po2Items[0].Id, ReceivedQty = 100 },
            new GrnItem { GrnId = grn2.Id, PoItemId = po2Items[1].Id, ReceivedQty = 100 },
            new GrnItem { GrnId = grn2.Id, PoItemId = po2Items[2].Id, ReceivedQty = 50 }
        };
        await context.GrnItems.AddRangeAsync(grn1Items);
        await context.GrnItems.AddRangeAsync(grn2Items);
        await context.SaveChangesAsync();

        // 18. Seed Sample Sales
        var mikeCashier = users[3];
        var sale1 = new Sale
        {
            OutletId = mainOutlet.Id, CustomerId = customers[1].Id, SaleDate = now.AddDays(-10),
            TerminalId = mainTerminal.Id,
            TotalAmount = 1089.97m, Discount = 0, Tax = 92.65m, PaymentMethod = "cash",
            Status = "completed", CashierId = mikeCashier.Id, CreatedAt = now.AddDays(-10)
        };
        var sale2 = new Sale
        {
            OutletId = mainOutlet.Id, CustomerId = customers[2].Id, SaleDate = now.AddDays(-7),
            TerminalId = mainTerminal.Id,
            TotalAmount = 89.97m, Discount = 5, Tax = 0, PaymentMethod = "card",
            Status = "completed", CashierId = mikeCashier.Id, CreatedAt = now.AddDays(-7)
        };
        var sale3 = new Sale
        {
            OutletId = branchOutlet.Id, CustomerId = customers[3].Id, SaleDate = now.AddDays(-3),
            TerminalId = branchTerminal.Id,
            TotalAmount = 749.97m, Discount = 10, Tax = 63.75m, PaymentMethod = "card",
            Status = "completed", CashierId = lisaManager.Id, CreatedAt = now.AddDays(-3)
        };
        await context.Sales.AddRangeAsync(sale1, sale2, sale3);
        await context.SaveChangesAsync();

        var sale1Items = new List<SaleItem>
        {
            new SaleItem { SaleId = sale1.Id, VariantId = laptopVariant.Id, Quantity = 1, UnitPrice = 999.99m, Subtotal = 999.99m },
            new SaleItem { SaleId = sale1.Id, VariantId = cableVariant.Id, Quantity = 2, UnitPrice = 19.99m, Subtotal = 39.98m },
            new SaleItem { SaleId = sale1.Id, VariantId = coffeeVariant.Id, Quantity = 2, UnitPrice = 24.99m, Subtotal = 49.98m }
        };
        var sale2Items = new List<SaleItem>
        {
            new SaleItem { SaleId = sale2.Id, VariantId = tshirtVariants[0].Id, Quantity = 1, UnitPrice = 29.99m, Subtotal = 29.99m },
            new SaleItem { SaleId = sale2.Id, VariantId = tshirtVariants[1].Id, Quantity = 2, UnitPrice = 29.99m, Subtotal = 59.98m }
        };
        var sale3Items = new List<SaleItem>
        {
            new SaleItem { SaleId = sale3.Id, VariantId = phoneVariants[0].Id, Quantity = 1, UnitPrice = 699.99m, Subtotal = 699.99m },
            new SaleItem { SaleId = sale3.Id, VariantId = earbudsVariants[0].Id, Quantity = 1, UnitPrice = 89.99m, Subtotal = 89.99m }
        };
        await context.SaleItems.AddRangeAsync(sale1Items);
        await context.SaleItems.AddRangeAsync(sale2Items);
        await context.SaleItems.AddRangeAsync(sale3Items);
        await context.SaveChangesAsync();

        // 19. Seed Stock Adjustments
        var stockAdjustments = new List<StockAdjustment>
        {
            new StockAdjustment
            {
                AdjustmentNumber = "ADJ-2026-00001",
                Status = StockAdjustment.StatusApproved,
                LocationId = mainOutlet.Id,
                LocationType = "outlet",
                AdjustedBy = sarahManager.Id,
                AdjustmentDate = now.AddDays(-8),
                CreatedAt = now.AddDays(-8),
                UpdatedAt = now.AddDays(-8),
                SubmittedAt = now.AddDays(-8),
                ApprovedBy = sarahManager.Id,
                ApprovedAt = now.AddDays(-8),
                Lines = new List<StockAdjustmentLine>
                {
                    new StockAdjustmentLine
                    {
                        VariantId = coffeeVariant.Id,
                        PreviousQuantity = 25,
                        QuantityChange = -5,
                        NewQuantity = 20,
                        Reason = "Damaged",
                        Notes = "Water damage",
                        CreatedAt = now.AddDays(-8),
                        UpdatedAt = now.AddDays(-8)
                    }
                }
            },
            new StockAdjustment
            {
                AdjustmentNumber = "ADJ-2026-00002",
                Status = StockAdjustment.StatusApproved,
                LocationId = centralWarehouse.Id,
                LocationType = "warehouse",
                AdjustedBy = tomStock.Id,
                AdjustmentDate = now.AddDays(-6),
                CreatedAt = now.AddDays(-6),
                UpdatedAt = now.AddDays(-6),
                SubmittedAt = now.AddDays(-6),
                ApprovedBy = sarahManager.Id,
                ApprovedAt = now.AddDays(-6),
                Lines = new List<StockAdjustmentLine>
                {
                    new StockAdjustmentLine
                    {
                        VariantId = cableVariant.Id,
                        PreviousQuantity = 150,
                        QuantityChange = 50,
                        NewQuantity = 200,
                        Reason = "StockCountCorrection",
                        Notes = "After physical audit",
                        CreatedAt = now.AddDays(-6),
                        UpdatedAt = now.AddDays(-6)
                    }
                }
            }
        };
        await context.StockAdjustments.AddRangeAsync(stockAdjustments);
        await context.SaveChangesAsync();

        // 20. Seed Stock Transfer
        var stockTransfer = new StockTransfer
        {
            FromLocationId = centralWarehouse.Id, FromLocationType = "warehouse",
            ToLocationId = branchOutlet.Id, ToLocationType = "outlet",
            TransferDate = now.AddDays(-4), Status = "completed",
            ApprovedBy = sarahManager.Id, CreatedBy = tomStock.Id, CreatedAt = now.AddDays(-4)
        };
        await context.StockTransfers.AddAsync(stockTransfer);
        await context.SaveChangesAsync();

        var transferItems = new List<StockTransferItem>
        {
            new StockTransferItem { TransferId = stockTransfer.Id, VariantId = tshirtVariants[3].Id, Quantity = 20 },
            new StockTransferItem { TransferId = stockTransfer.Id, VariantId = dressVariants[2].Id, Quantity = 10 }
        };
        await context.StockTransferItems.AddRangeAsync(transferItems);
        await context.SaveChangesAsync();

        // 21. Seed Expenses
        var expenses = new List<Expense>
        {
            new Expense { Category = "Utilities", Amount = 850m, Description = "Monthly electricity bill", ExpenseDate = now.AddDays(-5), OutletId = mainOutlet.Id },
            new Expense { Category = "Rent", Amount = 5000m, Description = "Monthly store rent", ExpenseDate = now.AddDays(-1), OutletId = mainOutlet.Id },
            new Expense { Category = "Utilities", Amount = 620m, Description = "Monthly electricity bill", ExpenseDate = now.AddDays(-5), OutletId = branchOutlet.Id },
            new Expense { Category = "Marketing", Amount = 1200m, Description = "Social media advertising campaign", ExpenseDate = now.AddDays(-12), OutletId = null },
            new Expense { Category = "Supplies", Amount = 300m, Description = "Office supplies and packaging materials", ExpenseDate = now.AddDays(-8), OutletId = mainOutlet.Id }
        };
        await context.Expenses.AddRangeAsync(expenses);
        await context.SaveChangesAsync();

        // 22. Seed Bills (payables to suppliers)
        var bills = new List<Bill>
        {
            new Bill { SupplierId = techSupplier.Id, PoId = po1.Id, AmountDue = 18500m, DueDate = now.AddDays(30), Status = "paid" },
            new Bill { SupplierId = fashionSupplier.Id, PoId = po2.Id, AmountDue = 5500m, DueDate = now.AddDays(15), Status = "unpaid" },
            new Bill { SupplierId = foodSupplier.Id, PoId = null, AmountDue = 2400m, DueDate = now.AddDays(20), Status = "unpaid" }
        };
        await context.Bills.AddRangeAsync(bills);
        await context.SaveChangesAsync();

        // 23. Seed Transactions (accounting ledger entries)
        var transactions = new List<Transaction>
        {
            new Transaction { AccountId = cashAccount.Id, Amount = 1089.97m, Type = "credit", Description = "Sale #1 - Cash payment received", TransactionDate = now.AddDays(-10), ReferenceId = sale1.Id, ReferenceType = "sale" },
            new Transaction { AccountId = bankAccount.Id, Amount = 89.97m, Type = "credit", Description = "Sale #2 - Card payment received", TransactionDate = now.AddDays(-7), ReferenceId = sale2.Id, ReferenceType = "sale" },
            new Transaction { AccountId = bankAccount.Id, Amount = 749.97m, Type = "credit", Description = "Sale #3 - Card payment received", TransactionDate = now.AddDays(-3), ReferenceId = sale3.Id, ReferenceType = "sale" },
            new Transaction { AccountId = bankAccount.Id, Amount = 18500m, Type = "debit", Description = "Payment to TechSupply Co. for PO#1", TransactionDate = now.AddDays(-18), ReferenceId = bills[0].Id, ReferenceType = "bill" },
            new Transaction { AccountId = bankAccount.Id, Amount = 850m, Type = "debit", Description = "Electricity bill - Main Store", TransactionDate = now.AddDays(-5), ReferenceId = expenses[0].Id, ReferenceType = "expense" },
            new Transaction { AccountId = bankAccount.Id, Amount = 5000m, Type = "debit", Description = "Monthly rent - Main Store", TransactionDate = now.AddDays(-1), ReferenceId = expenses[1].Id, ReferenceType = "expense" }
        };
        await context.Transactions.AddRangeAsync(transactions);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Idempotently ensures the BusinessOwner role and a default Business Owner user exist.
    /// Safe to call on already-seeded databases — only inserts what's missing.
    /// </summary>
    private static async Task EnsureBusinessOwnerAsync(RetailPOSDbContext context)
    {
        var ownerRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "BusinessOwner");
        if (ownerRole == null)
        {
            ownerRole = new Role
            {
                Name = "BusinessOwner",
                Permissions = "[\"*\"]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Roles.Add(ownerRole);
            await context.SaveChangesAsync();
        }

        var ownerUserExists = await context.Users.AnyAsync(u => u.Email == "owner@retailpos.com");
        if (!ownerUserExists)
        {
            context.Users.Add(new User
            {
                Name = "Business Owner",
                Email = "owner@retailpos.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123", workFactor: 12),
                RoleId = ownerRole.Id,
                OutletId = null,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }
    }

    private static async Task EnsureDefaultPosTerminalsAsync(RetailPOSDbContext context)
    {
        var outlets = await context.Outlets.AsNoTracking().ToListAsync();
        if (outlets.Count == 0)
            return;

        foreach (var outlet in outlets)
        {
            var hasTerminal = await context.PosTerminals.AnyAsync(t => t.OutletId == outlet.Id);
            if (hasTerminal)
                continue;

            context.PosTerminals.Add(new PosTerminal
            {
                OutletId = outlet.Id,
                Name = $"{outlet.Name} Counter 1",
                Code = $"OUTLET-{outlet.Id:D2}",
                IsActive = true,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Gets the default roles with their permissions.
    /// </summary>
    private static List<Role> GetDefaultRoles()
    {
        return new List<Role>
        {
            new Role
            {
                Name = "Super Admin",
                Permissions = "[\"*\"]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Role
            {
                Name = "BusinessOwner",
                Permissions = "[\"*\"]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Role
            {
                Name = "OutletManager",
                Permissions = @"[
                    ""products.view"", ""products.create"", ""products.edit"",
                    ""categories.view"",
                    ""inventory.view"", ""inventory.create"", ""inventory.edit"",
                    ""stock_adjustments.view"", ""stock_adjustments.create"",
                    ""StockCount.ViewOwn"", ""StockCount.Create"", ""StockCount.Download"", ""StockCount.Print"", ""StockCount.Upload"", ""StockCount.Submit"", ""StockCount.Reject"", ""StockCount.Reopen"",
                    ""stock_requisitions.view"", ""stock_requisitions.create"", ""stock_requisitions.edit"", ""stock_requisitions.approve"", ""stock_requisitions.reject"", ""stock_requisitions.convert_to_transfer"",
                    ""stock_transfers.view"", ""stock_transfers.create"", ""stock_transfers.dispatch"", ""stock_transfers.receive"", ""stock_transfers.reject_receive"", ""stock_transfers.return_create"",
                    ""low_stock_alerts.view"",
                    ""sales.view"", ""sales.create"",
                    ""purchases.view"", ""purchases.create"",
                    ""customers.view"", ""customers.create"", ""customers.edit"",
                    ""suppliers.view"",
                    ""reports.view""
                ]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Role
            {
                Name = "Salesman",
                Permissions = @"[
                    ""products.view"",
                    ""inventory.view"",
                    ""stock_requisitions.view"",
                    ""stock_transfers.view"",
                    ""low_stock_alerts.view"",
                    ""sales.view"", ""sales.create"",
                    ""customers.view"", ""customers.create""
                ]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Role
            {
                Name = "Admin",
                Permissions = @"[
                    ""users.view"", ""users.create"", ""users.edit"", ""users.delete"",
                    ""roles.view"", ""roles.create"", ""roles.edit"", ""roles.delete"",
                    ""products.view"", ""products.create"", ""products.edit"", ""products.delete"",
                    ""categories.view"", ""categories.create"", ""categories.edit"", ""categories.delete"",
                    ""inventory.view"", ""inventory.create"", ""inventory.edit"", ""inventory.delete"",
                    ""stock_adjustments.view"", ""stock_adjustments.create"", ""stock_adjustments.edit"", ""stock_adjustments.delete"", ""stock_adjustments.approve"", ""stock_adjustments.cancel"",
                    ""StockCount.ViewOwn"", ""StockCount.ViewAll"", ""StockCount.Create"", ""StockCount.Download"", ""StockCount.Print"", ""StockCount.Upload"", ""StockCount.Submit"", ""StockCount.Approve"", ""StockCount.Reject"", ""StockCount.Reopen"",
                    ""stock_transfers.view"", ""stock_transfers.create"", ""stock_transfers.edit"", ""stock_transfers.delete"", ""stock_transfers.approve"", ""stock_transfers.cancel"", ""stock_transfers.dispatch"", ""stock_transfers.receive"", ""stock_transfers.reject_receive"", ""stock_transfers.return_create"", ""stock_transfers.transfer_from_any_location"",
                    ""stock_requisitions.view"", ""stock_requisitions.create"", ""stock_requisitions.edit"", ""stock_requisitions.approve"", ""stock_requisitions.reject"", ""stock_requisitions.convert_to_transfer"",
                    ""low_stock_alerts.view"", ""low_stock_alerts.create"", ""low_stock_alerts.edit"", ""low_stock_alerts.delete"",
                    ""sales.view"", ""sales.create"",
                    ""purchases.view"", ""purchases.create"", ""purchases.edit"", ""purchases.delete"",
                    ""customers.view"", ""customers.create"", ""customers.edit"", ""customers.delete"",
                    ""suppliers.view"", ""suppliers.create"", ""suppliers.edit"", ""suppliers.delete"",
                    ""outlets.view"", ""outlets.create"", ""outlets.edit"", ""outlets.delete"",
                    ""warehouses.view"", ""warehouses.create"", ""warehouses.edit"", ""warehouses.delete"",
                    ""reports.view"", ""reports.export"",
                    ""settings.view"", ""settings.edit""
                ]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Role
            {
                Name = "Manager",
                Permissions = @"[
                    ""products.view"", ""products.create"", ""products.edit"",
                    ""categories.view"", ""categories.create"", ""categories.edit"",
                    ""inventory.view"", ""inventory.create"", ""inventory.edit"",
                    ""stock_adjustments.view"", ""stock_adjustments.create"",
                    ""StockCount.ViewOwn"", ""StockCount.Create"", ""StockCount.Download"", ""StockCount.Print"", ""StockCount.Upload"", ""StockCount.Submit"", ""StockCount.Reject"", ""StockCount.Reopen"",
                    ""stock_requisitions.view"", ""stock_requisitions.create"", ""stock_requisitions.edit"", ""stock_requisitions.approve"", ""stock_requisitions.reject"", ""stock_requisitions.convert_to_transfer"",
                    ""stock_transfers.view"", ""stock_transfers.create"", ""stock_transfers.dispatch"", ""stock_transfers.receive"", ""stock_transfers.reject_receive"", ""stock_transfers.return_create"",
                    ""low_stock_alerts.view"",
                    ""sales.view"", ""sales.create"",
                    ""purchases.view"", ""purchases.create"",
                    ""customers.view"", ""customers.create"", ""customers.edit"",
                    ""suppliers.view"", ""suppliers.create"", ""suppliers.edit"",
                    ""reports.view""
                ]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Role
            {
                Name = "Cashier",
                Permissions = @"[
                    ""products.view"",
                    ""inventory.view"",
                    ""low_stock_alerts.view"",
                    ""sales.view"", ""sales.create"",
                    ""customers.view"", ""customers.create""
                ]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Role
            {
                Name = "Stock Manager",
                Permissions = @"[
                    ""products.view"",
                    ""categories.view"",
                    ""inventory.view"", ""inventory.create"", ""inventory.edit"", ""inventory.delete"",
                    ""stock_adjustments.view"", ""stock_adjustments.create"", ""stock_adjustments.edit"", ""stock_adjustments.delete"", ""stock_adjustments.approve"", ""stock_adjustments.cancel"",
                    ""StockCount.ViewOwn"", ""StockCount.Create"", ""StockCount.Download"", ""StockCount.Print"", ""StockCount.Upload"", ""StockCount.Submit"", ""StockCount.Reject"", ""StockCount.Reopen"",
                    ""stock_transfers.view"", ""stock_transfers.create"", ""stock_transfers.edit"", ""stock_transfers.delete"", ""stock_transfers.approve"", ""stock_transfers.cancel"", ""stock_transfers.dispatch"", ""stock_transfers.receive"", ""stock_transfers.reject_receive"", ""stock_transfers.return_create"", ""stock_transfers.transfer_from_any_location"",
                    ""stock_requisitions.view"", ""stock_requisitions.create"", ""stock_requisitions.edit"", ""stock_requisitions.approve"", ""stock_requisitions.reject"", ""stock_requisitions.convert_to_transfer"",
                    ""low_stock_alerts.view"", ""low_stock_alerts.edit"",
                    ""purchases.view"", ""purchases.create"", ""purchases.edit"",
                    ""suppliers.view"", ""suppliers.create"", ""suppliers.edit"",
                    ""warehouses.view"",
                    ""reports.view""
                ]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Role
            {
                Name = "User",
                Permissions = @"[
                    ""products.view"",
                    ""inventory.view"",
                    ""low_stock_alerts.view"",
                    ""sales.view"",
                    ""reports.view""
                ]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };
    }
}
