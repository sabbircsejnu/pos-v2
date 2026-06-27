using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Business;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

public class BusinessOnboardingService : IBusinessOnboardingService
{
    private static readonly TimeSpan InvitationTtl = TimeSpan.FromDays(7);

    private readonly RetailPOSDbContext _db;
    private readonly ILogger<BusinessOnboardingService> _logger;

    public BusinessOnboardingService(RetailPOSDbContext db, ILogger<BusinessOnboardingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CreateBusinessResponseDto> CreateBusinessAsync(CreateBusinessRequestDto request)
    {
        ValidateRequest(request);

        var normalizedEmails = new[]
        {
            request.OwnerEmail,
            request.OutletManagerEmail,
            request.SalesPersonEmail,
            request.AccountsAdminEmail,
            request.WarehouseManagerEmail
        }
        .Where(e => !string.IsNullOrWhiteSpace(e))
        .Select(e => e!.Trim().ToLowerInvariant())
        .ToList();

        var duplicateEmailsInPayload = normalizedEmails
            .GroupBy(e => e)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateEmailsInPayload.Count > 0)
            throw new InvalidOperationException($"Duplicate emails in request: {string.Join(", ", duplicateEmailsInPayload)}");

        var existingEmails = await _db.Users
            .Where(u => normalizedEmails.Contains(u.Email.ToLower()))
            .Select(u => u.Email)
            .ToListAsync();

        if (existingEmails.Count > 0)
            throw new InvalidOperationException($"Email already exists: {string.Join(", ", existingEmails)}");

        var existingBusiness = await _db.Businesses
            .AnyAsync(b => b.Name.ToLower() == request.BusinessName.Trim().ToLower());
        if (existingBusiness)
            throw new InvalidOperationException("Business name already exists.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var business = new Business
            {
                Name = request.BusinessName.Trim(),
                Email = request.BusinessEmail?.Trim(),
                Phone = request.BusinessPhone?.Trim(),
                Address = request.BusinessAddress?.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Businesses.Add(business);
            await _db.SaveChangesAsync();

            var defaultFeatures = new[]
            {
                "sales.hold",
                "sales.refund",
                "stock_transfers.enabled",
                "stock_adjustments.enabled",
                "reports.export"
            };

            _db.BusinessFeatureSettings.AddRange(defaultFeatures.Select(feature => new BusinessFeatureSetting
            {
                BusinessId = business.Id,
                FeatureKey = feature,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }));
            await _db.SaveChangesAsync();

            var outlet = new Outlet
            {
                BusinessId = business.Id,
                Name = string.IsNullOrWhiteSpace(request.DefaultOutletName) ? "Main Outlet" : request.DefaultOutletName.Trim(),
                Address = request.BusinessAddress?.Trim() ?? "Not provided",
                ContactNumber = request.BusinessPhone?.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Outlets.Add(outlet);
            await _db.SaveChangesAsync();

            Warehouse? warehouse = null;
            if (request.CreateDefaultWarehouse)
            {
                warehouse = new Warehouse
                {
                    BusinessId = business.Id,
                    Name = string.IsNullOrWhiteSpace(request.DefaultWarehouseName)
                        ? "Main Warehouse"
                        : request.DefaultWarehouseName.Trim(),
                    Address = request.BusinessAddress?.Trim() ?? "Not provided",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.Warehouses.Add(warehouse);
                await _db.SaveChangesAsync();
            }

            var ownerRole = await EnsureRoleAsync("BusinessOwner", Permissions.BusinessOwner);
            var outletManagerRole = await EnsureRoleAsync("OutletManager", Permissions.OutletManager);
            var salesRole = await EnsureRoleAsync("SalesPerson", Permissions.SalesPerson);
            var accountsRole = await EnsureRoleAsync("AccountsAdmin", Permissions.AccountsAdmin);
            Role? warehouseManagerRole = null;
            if (request.CreateDefaultWarehouse)
                warehouseManagerRole = await EnsureRoleAsync("WarehouseManager", Permissions.WarehouseManager);

            var onboardedUsers = new List<OnboardedUserDto>();

            var owner = await CreateUserAsync(
                business.Id,
                request.OwnerName,
                request.OwnerEmail,
                ownerRole.Id,
                null,
                "BusinessOwner",
                onboardedUsers);

            var outletManager = await CreateUserAsync(
                business.Id,
                request.OutletManagerName,
                request.OutletManagerEmail,
                outletManagerRole.Id,
                outlet.Id,
                "OutletManager",
                onboardedUsers);

            var salesPerson = await CreateUserAsync(
                business.Id,
                request.SalesPersonName,
                request.SalesPersonEmail,
                salesRole.Id,
                outlet.Id,
                "SalesPerson",
                onboardedUsers);

            var accountsAdmin = await CreateUserAsync(
                business.Id,
                request.AccountsAdminName,
                request.AccountsAdminEmail,
                accountsRole.Id,
                null,
                "AccountsAdmin",
                onboardedUsers);

            outlet.ManagerId = outletManager.Id;
            if (warehouse != null && warehouseManagerRole != null)
            {
                var warehouseManager = await CreateUserAsync(
                    business.Id,
                    request.WarehouseManagerName!,
                    request.WarehouseManagerEmail!,
                    warehouseManagerRole.Id,
                    null,
                    "WarehouseManager",
                    onboardedUsers);

                warehouse.ManagerId = warehouseManager.Id;
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Business {BusinessName} ({BusinessId}) onboarded successfully", business.Name, business.Id);

            return new CreateBusinessResponseDto
            {
                BusinessId = business.Id,
                BusinessName = business.Name,
                DefaultOutletId = outlet.Id,
                DefaultWarehouseId = warehouse?.Id,
                Users = onboardedUsers
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<List<BusinessSummaryDto>> GetBusinessesAsync(string? search = null, bool? isActive = null)
    {
        var query = _db.Businesses
            .AsNoTracking()
            .Include(b => b.Users)
                .ThenInclude(u => u.Role)
            .Include(b => b.Outlets)
            .Include(b => b.Warehouses)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(b =>
                b.Name.ToLower().Contains(term) ||
                (b.Email != null && b.Email.ToLower().Contains(term)) ||
                (b.Phone != null && b.Phone.ToLower().Contains(term)));
        }

        if (isActive.HasValue)
            query = query.Where(b => b.IsActive == isActive.Value);

        var businesses = await query
            .OrderBy(b => b.Name)
            .ToListAsync();

        return businesses.Select(MapBusinessSummary).ToList();
    }

    public async Task<BusinessSummaryDto> GetBusinessByIdAsync(long businessId)
    {
        var business = await _db.Businesses
            .AsNoTracking()
            .Include(b => b.Users).ThenInclude(u => u.Role)
            .Include(b => b.Outlets)
            .Include(b => b.Warehouses)
            .FirstOrDefaultAsync(b => b.Id == businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        return MapBusinessSummary(business);
    }

    public async Task<BusinessSummaryDto> SetBusinessActiveAsync(long businessId, bool isActive)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        business.IsActive = isActive;
        business.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var loaded = await _db.Businesses
            .AsNoTracking()
            .Include(b => b.Users).ThenInclude(u => u.Role)
            .Include(b => b.Outlets)
            .Include(b => b.Warehouses)
            .FirstAsync(b => b.Id == businessId);

        return MapBusinessSummary(loaded);
    }

    public async Task<BusinessSummaryDto> UpdateSubscriptionAsync(long businessId, UpdateBusinessSubscriptionDto request)
    {
        if (request.MaxOutlets.HasValue && request.MaxOutlets.Value < 0)
            throw new InvalidOperationException("MaxOutlets cannot be negative.");

        if (request.MaxUsers.HasValue && request.MaxUsers.Value < 0)
            throw new InvalidOperationException("MaxUsers cannot be negative.");

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        business.SubscriptionPlan = string.IsNullOrWhiteSpace(request.SubscriptionPlan)
            ? null
            : request.SubscriptionPlan.Trim();
        business.TrialEndsAt = request.TrialEndsAt;
        business.SubscriptionEndsAt = request.SubscriptionEndsAt;
        business.MaxOutlets = request.MaxOutlets;
        business.MaxUsers = request.MaxUsers;
        business.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var loaded = await _db.Businesses
            .AsNoTracking()
            .Include(b => b.Users).ThenInclude(u => u.Role)
            .Include(b => b.Outlets)
            .Include(b => b.Warehouses)
            .FirstAsync(b => b.Id == businessId);

        return MapBusinessSummary(loaded);
    }

    public async Task<List<BusinessFeatureSettingDto>> GetFeatureSettingsAsync(long businessId)
    {
        var businessExists = await _db.Businesses.AnyAsync(b => b.Id == businessId);
        if (!businessExists)
            throw new KeyNotFoundException("Business not found.");

        var features = await _db.BusinessFeatureSettings
            .AsNoTracking()
            .Where(f => f.BusinessId == businessId)
            .OrderBy(f => f.FeatureKey)
            .Select(f => new BusinessFeatureSettingDto
            {
                FeatureKey = f.FeatureKey,
                IsEnabled = f.IsEnabled,
                LimitValue = f.LimitValue
            })
            .ToListAsync();

        return features;
    }

    public async Task<List<BusinessFeatureSettingDto>> UpsertFeatureSettingsAsync(long businessId, UpdateBusinessFeatureSettingsDto request)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        if (request.Features.Count == 0)
            return await GetFeatureSettingsAsync(businessId);

        var keys = request.Features
            .Select(f => f.FeatureKey.Trim().ToLowerInvariant())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct()
            .ToList();

        var existing = await _db.BusinessFeatureSettings
            .Where(f => f.BusinessId == businessId && keys.Contains(f.FeatureKey))
            .ToDictionaryAsync(f => f.FeatureKey, StringComparer.OrdinalIgnoreCase);

        foreach (var feature in request.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.FeatureKey))
                continue;

            var key = feature.FeatureKey.Trim().ToLowerInvariant();

            if (existing.TryGetValue(key, out var row))
            {
                row.IsEnabled = feature.IsEnabled;
                row.LimitValue = feature.LimitValue;
                row.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
                {
                    BusinessId = businessId,
                    FeatureKey = key,
                    IsEnabled = feature.IsEnabled,
                    LimitValue = feature.LimitValue,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        business.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GetFeatureSettingsAsync(businessId);
    }

    public async Task<ResetBusinessOwnerAccessResponseDto> ResetBusinessOwnerAccessAsync(long businessId)
    {
        var businessExists = await _db.Businesses.AnyAsync(b => b.Id == businessId);
        if (!businessExists)
            throw new KeyNotFoundException("Business not found.");

        var owner = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u =>
                u.BusinessId == businessId &&
                u.Role != null &&
                u.Role.Name == "BusinessOwner")
            ?? throw new InvalidOperationException("No BusinessOwner user found for this business.");

        var now = DateTime.UtcNow;
        var activeInvitations = await _db.UserInvitations
            .Where(i => i.UserId == owner.Id && i.ConsumedAt == null && i.ExpiresAt > now)
            .ToListAsync();

        foreach (var invite in activeInvitations)
            invite.ConsumedAt = now;

        var rawToken = GenerateInvitationToken();
        var invitation = new UserInvitation
        {
            UserId = owner.Id,
            Purpose = "owner-reset",
            TokenHash = HashToken(rawToken),
            ExpiresAt = now.Add(InvitationTtl),
            CreatedAt = now
        };

        owner.MustResetPassword = true;
        owner.UpdatedAt = now;

        _db.UserInvitations.Add(invitation);
        await _db.SaveChangesAsync();

        return new ResetBusinessOwnerAccessResponseDto
        {
            UserId = owner.Id,
            OwnerEmail = owner.Email,
            InvitationToken = rawToken,
            InvitationExpiresAt = invitation.ExpiresAt
        };
    }

    private async Task<User> CreateUserAsync(
        long businessId,
        string name,
        string email,
        long roleId,
        long? outletId,
        string roleName,
        List<OnboardedUserDto> onboardedUsers)
    {
        var tempPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var user = new User
        {
            BusinessId = businessId,
            Name = name.Trim(),
            Email = email.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword, workFactor: 11),
            RoleId = roleId,
            OutletId = outletId,
            IsActive = true,
            MustResetPassword = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var rawToken = GenerateInvitationToken();
        var invitation = new UserInvitation
        {
            UserId = user.Id,
            Purpose = "onboarding",
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTime.UtcNow.Add(InvitationTtl),
            CreatedAt = DateTime.UtcNow
        };

        _db.UserInvitations.Add(invitation);
        await _db.SaveChangesAsync();

        onboardedUsers.Add(new OnboardedUserDto
        {
            UserId = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = roleName,
            InvitationToken = rawToken,
            InvitationExpiresAt = invitation.ExpiresAt
        });

        return user;
    }

    private async Task<Role> EnsureRoleAsync(string roleName, IReadOnlyList<string> permissions)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role != null)
        {
            List<string>? existingPermissions;
            try
            {
                existingPermissions = JsonSerializer.Deserialize<List<string>>(role.Permissions);
            }
            catch
            {
                existingPermissions = new List<string>();
            }

            existingPermissions ??= new List<string>();

            var required = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            var current = new HashSet<string>(existingPermissions, StringComparer.OrdinalIgnoreCase);

            if (!required.IsSubsetOf(current))
            {
                foreach (var p in required)
                    current.Add(p);

                role.Permissions = JsonSerializer.Serialize(current.OrderBy(p => p).ToList());
                role.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return role;
        }

        role = new Role
        {
            Name = roleName,
            Permissions = JsonSerializer.Serialize(permissions),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        return role;
    }

    private static BusinessSummaryDto MapBusinessSummary(Business business)
    {
        var owner = business.Users
            .FirstOrDefault(u => string.Equals(u.Role?.Name, "BusinessOwner", StringComparison.OrdinalIgnoreCase));

        return new BusinessSummaryDto
        {
            BusinessId = business.Id,
            BusinessName = business.Name,
            BusinessEmail = business.Email,
            BusinessPhone = business.Phone,
            IsActive = business.IsActive,
            SubscriptionPlan = business.SubscriptionPlan,
            TrialEndsAt = business.TrialEndsAt,
            SubscriptionEndsAt = business.SubscriptionEndsAt,
            MaxOutlets = business.MaxOutlets,
            MaxUsers = business.MaxUsers,
            UserCount = business.Users.LongCount(),
            OutletCount = business.Outlets.LongCount(),
            WarehouseCount = business.Warehouses.LongCount(),
            BusinessOwnerEmail = owner?.Email
        };
    }

    private static void ValidateRequest(CreateBusinessRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessName))
            throw new InvalidOperationException("BusinessName is required.");

        if (string.IsNullOrWhiteSpace(request.OwnerName) || string.IsNullOrWhiteSpace(request.OwnerEmail))
            throw new InvalidOperationException("Business owner name and email are required.");

        if (string.IsNullOrWhiteSpace(request.OutletManagerName) || string.IsNullOrWhiteSpace(request.OutletManagerEmail))
            throw new InvalidOperationException("Outlet manager name and email are required.");

        if (string.IsNullOrWhiteSpace(request.SalesPersonName) || string.IsNullOrWhiteSpace(request.SalesPersonEmail))
            throw new InvalidOperationException("Sales person name and email are required.");

        if (string.IsNullOrWhiteSpace(request.AccountsAdminName) || string.IsNullOrWhiteSpace(request.AccountsAdminEmail))
            throw new InvalidOperationException("Accounts admin name and email are required.");

        if (request.CreateDefaultWarehouse &&
            (string.IsNullOrWhiteSpace(request.WarehouseManagerName) || string.IsNullOrWhiteSpace(request.WarehouseManagerEmail)))
        {
            throw new InvalidOperationException(
                "Warehouse manager name and email are required when CreateDefaultWarehouse is true.");
        }
    }

    private static string GenerateInvitationToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static class Permissions
    {
        public static readonly IReadOnlyList<string> BusinessOwner = new[] { "*" };
        public static readonly IReadOnlyList<string> OutletManager = new[]
        {
            "users.view",
            "products.view", "products.create", "products.edit",
            "categories.view",
            "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
            "stock_transfers.view", "stock_transfers.create", "stock_transfers.approve", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create",
            "stock_adjustments.view", "stock_adjustments.create",
            "sales.view", "sales.create",
            "reports.sales"
        };
        public static readonly IReadOnlyList<string> SalesPerson = new[]
        {
            "products.view",
            "stock_requisitions.view",
            "stock_transfers.view",
            "sales.view", "sales.create",
            "customers.view", "customers.create"
        };
        public static readonly IReadOnlyList<string> AccountsAdmin = new[]
        {
            "accounts.view", "accounts.create", "accounts.edit", "accounts.delete",
            "expenses.view", "expenses.create", "expenses.edit",
            "transactions.view", "transactions.create", "transactions.edit", "transactions.delete",
            "reports.financial"
        };
        public static readonly IReadOnlyList<string> WarehouseManager = new[]
        {
            "inventory.view", "inventory.edit",
            "stock_adjustments.view", "stock_adjustments.create",
            "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
            "stock_transfers.view", "stock_transfers.create", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
            "low_stock_alerts.view",
            "warehouses.view",
            "purchases.view", "purchases.create", "purchases.receive"
        };
    }
 }
