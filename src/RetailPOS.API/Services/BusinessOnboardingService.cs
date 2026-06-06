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
            "sales.view", "sales.create",
            "reports.sales"
        };
        public static readonly IReadOnlyList<string> SalesPerson = new[]
        {
            "products.view",
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
            "inventory.view", "inventory.adjust", "inventory.transfer",
            "warehouses.view",
            "purchases.view", "purchases.create"
        };
    }
 }
