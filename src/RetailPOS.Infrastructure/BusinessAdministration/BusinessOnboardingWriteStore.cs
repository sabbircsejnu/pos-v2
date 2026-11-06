using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.BusinessAdministration;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.Commands;
using RetailPOS.Application.BusinessAdministration.ReadModels;
using RetailPOS.Application.Integration;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingConfigurations;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Integration;

namespace RetailPOS.Infrastructure.BusinessAdministration;

public sealed class BusinessOnboardingWriteStore(
    RetailPOSDbContext dbContext,
    AccountingConfigurationProvisioner accountingConfigurationProvisioner,
    IPasswordHasher passwordHasher,
    IAuditService auditService,
    IOperationalLocationSourceEventWriter sourceEvents,
    ILogger<BusinessOnboardingWriteStore> logger) : IBusinessOnboardingWriteStore
{
    private static readonly TimeSpan InvitationTtl = TimeSpan.FromDays(7);
    private static readonly string[] DefaultFeatures =
    [
        "sales.hold",
        "sales.refund",
        "stock_transfers.enabled",
        "stock_adjustments.enabled",
        "reports.export"
    ];

    public async Task<BusinessOnboardingWriteResult> CreateBusinessAsync(
        CreateBusinessCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmails = new[]
            {
                request.OwnerEmail,
                request.OutletManagerEmail,
                request.SalesPersonEmail,
                request.AccountsAdminEmail,
                request.WarehouseManagerEmail
            }
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email!.Trim().ToLowerInvariant())
            .ToList();

        var duplicateEmails = normalizedEmails
            .GroupBy(email => email)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateEmails.Count > 0)
            return new BusinessOnboardingWriteResult(
                BusinessOnboardingStatus.DuplicateEmailsInRequest,
                Emails: duplicateEmails);

        var existingEmails = await dbContext.Users
            .Where(user => normalizedEmails.Contains(user.Email.ToLower()))
            .Select(user => user.Email)
            .ToListAsync(cancellationToken);
        if (existingEmails.Count > 0)
            return new BusinessOnboardingWriteResult(
                BusinessOnboardingStatus.ExistingEmails,
                Emails: existingEmails);

        var normalizedBusinessName = request.BusinessName.Trim().ToLower();
        var existingBusiness = await dbContext.Businesses
            .AnyAsync(business => business.Name.ToLower() == normalizedBusinessName, cancellationToken);
        if (existingBusiness)
            return new BusinessOnboardingWriteResult(BusinessOnboardingStatus.BusinessNameExists);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var business = new Business
            {
                Name = request.BusinessName.Trim(),
                Email = request.BusinessEmail?.Trim(),
                Phone = request.BusinessPhone?.Trim(),
                Address = request.BusinessAddress?.Trim(),
                IsActive = true,
                ShowPoweredByOnReceipt = request.ShowPoweredByOnReceipt,
                PoweredByText = request.PoweredByText,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.Businesses.Add(business);
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.HrEmploymentTypes.AddRange(HrEmploymentTypeCatalog.Defaults.Select(type =>
                new HrEmploymentType
                {
                    BusinessId = business.Id,
                    Code = type.Code,
                    Name = type.Name,
                    IsActive = true
                }));
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.BusinessFeatureSettings.AddRange(DefaultFeatures.Select(feature => new BusinessFeatureSetting
            {
                BusinessId = business.Id,
                FeatureKey = feature,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }));
            await dbContext.SaveChangesAsync(cancellationToken);

            var outlet = new Outlet
            {
                BusinessId = business.Id,
                Name = string.IsNullOrWhiteSpace(request.DefaultOutletName)
                    ? "Main Outlet"
                    : request.DefaultOutletName.Trim(),
                Address = request.BusinessAddress?.Trim() ?? "Not provided",
                ContactNumber = request.BusinessPhone?.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            dbContext.Outlets.Add(outlet);
            await dbContext.SaveChangesAsync(cancellationToken);
            await sourceEvents.AppendAsync("OutletCreated",
                OperationalLocationSourcePersistence.Snapshot(outlet), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

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
                dbContext.Warehouses.Add(warehouse);
                await dbContext.SaveChangesAsync(cancellationToken);
                await sourceEvents.AppendAsync("WarehouseCreated",
                    OperationalLocationSourcePersistence.Snapshot(warehouse), cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var ownerRole = await EnsureRoleAsync(
                "BusinessOwner",
                BusinessRolePermissions.BusinessOwner,
                cancellationToken);
            var outletManagerRole = await EnsureRoleAsync(
                "OutletManager",
                BusinessRolePermissions.OutletManager,
                cancellationToken);
            var salesRole = await EnsureRoleAsync(
                "SalesPerson",
                BusinessRolePermissions.SalesPerson,
                cancellationToken);
            var accountsRole = await EnsureRoleAsync(
                "AccountsAdmin",
                BusinessRolePermissions.AccountsAdmin,
                cancellationToken);
            _ = await EnsureRoleAsync(
                AccountingManagerPermissions.RoleName,
                AccountingManagerPermissions.All,
                cancellationToken);
            var warehouseManagerRole = request.CreateDefaultWarehouse
                ? await EnsureRoleAsync(
                    "WarehouseManager",
                    BusinessRolePermissions.WarehouseManager,
                    cancellationToken)
                : null;

            var onboardedUsers = new List<OnboardedUserReadModel>();
            var owner = await CreateUserAsync(
                business.Id,
                request.OwnerName,
                request.OwnerEmail,
                ownerRole.Id,
                null,
                "BusinessOwner",
                onboardedUsers,
                cancellationToken);
            var outletManager = await CreateUserAsync(
                business.Id,
                request.OutletManagerName,
                request.OutletManagerEmail,
                outletManagerRole.Id,
                outlet.Id,
                "OutletManager",
                onboardedUsers,
                cancellationToken);
            _ = await CreateUserAsync(
                business.Id,
                request.SalesPersonName,
                request.SalesPersonEmail,
                salesRole.Id,
                outlet.Id,
                "SalesPerson",
                onboardedUsers,
                cancellationToken);
            _ = await CreateUserAsync(
                business.Id,
                request.AccountsAdminName,
                request.AccountsAdminEmail,
                accountsRole.Id,
                null,
                "AccountsAdmin",
                onboardedUsers,
                cancellationToken);

            outlet.ManagerId = outletManager.Id;
            outlet.UpdatedAt = DateTime.UtcNow;
            outlet.SourceVersion = checked(outlet.SourceVersion + 1);
            await sourceEvents.AppendAsync("OutletUpdated",
                OperationalLocationSourcePersistence.Snapshot(outlet), cancellationToken);
            if (warehouse is not null && warehouseManagerRole is not null)
            {
                var warehouseManager = await CreateUserAsync(
                    business.Id,
                    request.WarehouseManagerName!,
                    request.WarehouseManagerEmail!,
                    warehouseManagerRole.Id,
                    null,
                    "WarehouseManager",
                    onboardedUsers,
                    cancellationToken);
                warehouse.ManagerId = warehouseManager.Id;
                warehouse.UpdatedAt = DateTime.UtcNow;
                warehouse.SourceVersion = checked(warehouse.SourceVersion + 1);
                await sourceEvents.AppendAsync("WarehouseUpdated",
                    OperationalLocationSourcePersistence.Snapshot(warehouse), cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            var accountingConfiguration = await accountingConfigurationProvisioner.EnsureInitializedAsync(
                business.Id,
                owner.Id,
                cancellationToken);
            await EnsureOutletAccountingConfigurationAsync(
                business.Id,
                outlet.Id,
                accountingConfiguration,
                owner.Id,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Business {BusinessName} ({BusinessId}) onboarded successfully",
                business.Name,
                business.Id);

            return new BusinessOnboardingWriteResult(
                BusinessOnboardingStatus.Created,
                new BusinessOnboardingReadModel(
                    business.Id,
                    business.Name,
                    outlet.Id,
                    warehouse?.Id,
                    onboardedUsers));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
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
        List<OnboardedUserReadModel> onboardedUsers,
        CancellationToken cancellationToken)
    {
        var temporaryPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var user = new User
        {
            BusinessId = businessId,
            Name = name.Trim(),
            Email = email.Trim(),
            PasswordHash = passwordHasher.Hash(temporaryPassword, workFactor: 11),
            RoleId = roleId,
            OutletId = outletId,
            IsActive = true,
            MustResetPassword = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var invitation = new UserInvitation
        {
            UserId = user.Id,
            Purpose = "onboarding",
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))),
            ExpiresAt = DateTime.UtcNow.Add(InvitationTtl),
            CreatedAt = DateTime.UtcNow
        };
        dbContext.UserInvitations.Add(invitation);
        await dbContext.SaveChangesAsync(cancellationToken);

        onboardedUsers.Add(new OnboardedUserReadModel(
            user.Id,
            user.Name,
            user.Email,
            roleName,
            rawToken,
            invitation.ExpiresAt));
        return user;
    }

    private async Task<Role> EnsureRoleAsync(
        string roleName,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles
            .FirstOrDefaultAsync(item => item.Name == roleName, cancellationToken);
        if (role is not null)
        {
            List<string>? existingPermissions;
            try
            {
                existingPermissions = JsonSerializer.Deserialize<List<string>>(role.Permissions);
            }
            catch (JsonException)
            {
                existingPermissions = [];
            }

            existingPermissions ??= [];
            var required = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            var current = new HashSet<string>(existingPermissions, StringComparer.OrdinalIgnoreCase);
            if (!required.IsSubsetOf(current))
            {
                foreach (var permission in required)
                    current.Add(permission);
                role.Permissions = JsonSerializer.Serialize(current.OrderBy(permission => permission).ToList());
                role.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
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
        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        return role;
    }

    private async Task EnsureOutletAccountingConfigurationAsync(
        long businessId,
        long outletId,
        AccountingConfiguration configuration,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Outlets.AnyAsync(
                outlet => outlet.Id == outletId && outlet.BusinessId == businessId,
                cancellationToken))
            throw new InvalidOperationException("Outlet not found in this business.");

        var row = await dbContext.AccountingOutletConfigurations
            .FirstOrDefaultAsync(
                item => item.BusinessId == businessId && item.OutletId == outletId,
                cancellationToken);
        if (row is not null)
            return;

        var now = DateTime.UtcNow;
        row = new AccountingOutletConfiguration
        {
            BusinessId = businessId,
            OutletId = outletId,
            OutletCashAccountId = configuration.CashAccountId,
            OutletPettyCashAccountId = configuration.PettyCashAccountId,
            OutletBankAccountId = configuration.DefaultBankAccountId,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId
        };
        dbContext.AccountingOutletConfigurations.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Outlet accounting configuration created: {outletId}",
            PrimaryEntity = ("AccountingOutletConfiguration", row.Id.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = businessId,
                ["outletId"] = outletId,
                ["actorUserId"] = actorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "AccountingOutletConfiguration",
                    row.Id.ToString(),
                    AuditOperationType.Insert,
                    [
                        (nameof(AccountingOutletConfiguration.OutletId), null, row.OutletId),
                        (nameof(AccountingOutletConfiguration.OutletCashAccountId), null, row.OutletCashAccountId),
                        (nameof(AccountingOutletConfiguration.OutletPettyCashAccountId), null, row.OutletPettyCashAccountId),
                        (nameof(AccountingOutletConfiguration.OutletBankAccountId), null, row.OutletBankAccountId)
                    ])
            ]
        }, cancellationToken);
    }
}
