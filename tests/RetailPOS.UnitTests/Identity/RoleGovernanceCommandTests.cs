using System.Text.Json;
using FluentValidation.TestHelper;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Commands;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Identity;

public sealed class RoleGovernanceCommandTests
{
    [Fact]
    public async Task CreateRole_NormalizesDuplicateAndLegacyPermissions_AndForwardsCancellation()
    {
        var store = new FakeRoleGovernanceStore();
        var cancellationToken = new CancellationTokenSource().Token;
        var handler = new CreateRoleCommandHandler(store, new FakeRoleGovernanceContext(false));

        var result = await handler.Handle(
            new CreateRoleCommand(
                "Cashier",
                [" sales.view ", "sales.view", "inventory.adjust"]),
            cancellationToken);

        Assert.Equal("Cashier", result.Name);
        Assert.Equal(
            new[]
            {
                "sales.view",
                "stock_adjustments.view",
                "stock_adjustments.create",
                "stock_adjustments.edit",
                "stock_adjustments.delete",
                "stock_adjustments.approve",
                "stock_adjustments.reject",
                "stock_adjustments.cancel"
            },
            result.Permissions);
        Assert.NotNull(store.CreatedRole);
        Assert.Equal(cancellationToken, store.LastCancellationToken);
        Assert.Equal(RoleGovernanceAction.Create, store.LastAudit?.Action);
        Assert.Equal(store.CreatedRole.Permissions, store.LastAudit?.NewPermissions);
    }

    [Fact]
    public async Task CreateRole_RejectsDuplicateNameBeforeMutation()
    {
        var store = new FakeRoleGovernanceStore { NameExists = true };
        var handler = new CreateRoleCommandHandler(store, new FakeRoleGovernanceContext(false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CreateRoleCommand("Existing", ["sales.view"]), CancellationToken.None));

        Assert.Equal("Role with name 'Existing' already exists", exception.Message);
        Assert.Null(store.CreatedRole);
        Assert.Equal(0, store.UpdateCount + store.DeleteCount);
    }

    [Fact]
    public async Task CreateRole_RejectsInvalidPermissionsBeforeMutation()
    {
        var store = new FakeRoleGovernanceStore();
        var handler = new CreateRoleCommandHandler(store, new FakeRoleGovernanceContext(false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new CreateRoleCommand("Invalid", ["not.a.permission"]),
                CancellationToken.None));

        Assert.Equal("Invalid permissions: not.a.permission", exception.Message);
        Assert.Null(store.CreatedRole);
        Assert.Equal(1, store.NameExistsCalls);
    }

    [Fact]
    public async Task CreateRole_RestrictsProtectedRoleToRealSuperAdmin()
    {
        var store = new FakeRoleGovernanceStore();
        var handler = new CreateRoleCommandHandler(store, new FakeRoleGovernanceContext(false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new CreateRoleCommand("super admin", ["*"]), CancellationToken.None));

        Assert.Equal(0, store.NameExistsCalls);
        Assert.Null(store.CreatedRole);
    }

    [Fact]
    public async Task UpdateRole_ReplacesPermissionsAndPreservesAuditBeforeAfterValues()
    {
        var role = new Role
        {
            Id = 7,
            Name = "Cashier",
            Permissions = """["sales.view","products.view"]""",
            CreatedAt = new DateTime(2025, 1, 1),
            UpdatedAt = new DateTime(2025, 1, 2)
        };
        var store = new FakeRoleGovernanceStore { Role = role };
        var handler = new UpdateRoleCommandHandler(store, new FakeRoleGovernanceContext(false));

        var result = await handler.Handle(
            new UpdateRoleCommand(7, "Cashier Updated", ["inventory.view"]),
            CancellationToken.None);

        Assert.Equal("Cashier Updated", result.Name);
        Assert.Equal(["inventory.view"], result.Permissions);
        Assert.Equal("""["sales.view","products.view"]""", store.LastAudit?.OldPermissions);
        Assert.Equal("""["inventory.view"]""", store.LastAudit?.NewPermissions);
        Assert.Equal("Cashier", store.LastAudit?.OldName);
        Assert.Equal("Cashier Updated", store.LastAudit?.NewName);
        Assert.Equal(1, store.UpdateCount);
    }

    [Fact]
    public async Task UpdateRole_RejectsProtectedRoleAndInvalidPermissionsBeforePersistence()
    {
        var role = new Role { Id = 8, Name = "Super Admin", Permissions = """["*"]""" };
        var store = new FakeRoleGovernanceStore { Role = role };
        var handler = new UpdateRoleCommandHandler(store, new FakeRoleGovernanceContext(false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new UpdateRoleCommand(8, "Super Admin", ["*"]), CancellationToken.None));

        Assert.Equal(0, store.NameExistsCalls);
        Assert.Equal(0, store.UpdateCount);

        store.Role = new Role { Id = 9, Name = "Cashier", Permissions = """["sales.view"]""" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UpdateRoleCommand(9, "Cashier", ["unknown.key"]), CancellationToken.None));

        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task DeleteRole_RejectsProtectedOrAssignedRolesBeforeDelete()
    {
        var store = new FakeRoleGovernanceStore
        {
            Role = new Role { Id = 1, Name = "Super Admin", Permissions = """["*"]""" }
        };
        var handler = new DeleteRoleCommandHandler(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteRoleCommand(1), CancellationToken.None));
        Assert.Equal(0, store.ActiveAssignmentChecks);
        Assert.Equal(0, store.DeleteCount);

        store.Role = new Role { Id = 2, Name = "Cashier", Permissions = """["sales.view"]""" };
        store.HasActiveAssignments = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DeleteRoleCommand(2), CancellationToken.None));
        Assert.Equal(1, store.ActiveAssignmentChecks);
        Assert.Equal(0, store.DeleteCount);
    }

    [Fact]
    public async Task DeleteRole_PersistsRoleAndAuditWithCancellationToken()
    {
        var store = new FakeRoleGovernanceStore
        {
            Role = new Role { Id = 3, Name = "Unused", Permissions = """["sales.view"]""" }
        };
        var handler = new DeleteRoleCommandHandler(store);
        var cancellationToken = new CancellationTokenSource().Token;

        var result = await handler.Handle(new DeleteRoleCommand(3), cancellationToken);

        Assert.True(result);
        Assert.Equal(1, store.DeleteCount);
        Assert.Equal(cancellationToken, store.LastCancellationToken);
        Assert.Equal(RoleGovernanceAction.Delete, store.LastAudit?.Action);
        Assert.Equal(store.Role.Name, store.LastAudit?.OldName);
        Assert.Equal(store.Role.Permissions, store.LastAudit?.OldPermissions);
        Assert.Null(store.LastAudit?.NewName);
        Assert.Null(store.LastAudit?.NewPermissions);
    }

    [Fact]
    public async Task CreateRoleValidator_UsesExistingRequestShapeRules()
    {
        var validator = new CreateRoleCommandValidator();

        var emptyName = await validator.TestValidateAsync(new CreateRoleCommand("", ["sales.view"]));
        emptyName.ShouldHaveValidationErrorFor(command => command.Name)
            .WithErrorMessage("Role name is required");

        var tooLongName = await validator.TestValidateAsync(
            new CreateRoleCommand(new string('x', 51), ["sales.view"]));
        tooLongName.ShouldHaveValidationErrorFor(command => command.Name)
            .WithErrorMessage("Role name cannot exceed 50 characters");

        var missingPermissions = await validator.TestValidateAsync(
            new CreateRoleCommand("Cashier", null));
        missingPermissions.ShouldHaveValidationErrorFor(command => command.Permissions)
            .WithErrorMessage("Permissions are required");

        var emptyPermissions = await validator.TestValidateAsync(
            new CreateRoleCommand("Cashier", []));
        emptyPermissions.ShouldNotHaveValidationErrorFor(command => command.Permissions);
    }

    private sealed class FakeRoleGovernanceContext(bool isSuperAdmin) : IRoleGovernanceContext
    {
        public bool IsSuperAdmin { get; } = isSuperAdmin;
    }

    private sealed class FakeRoleGovernanceStore : IRoleGovernanceStore
    {
        public Role Role { get; set; } = new();
        public Role? CreatedRole { get; private set; }
        public bool NameExists { get; set; }
        public bool HasActiveAssignments { get; set; }
        public int NameExistsCalls { get; private set; }
        public int ActiveAssignmentChecks { get; private set; }
        public int UpdateCount { get; private set; }
        public int DeleteCount { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public RoleGovernanceAudit? LastAudit { get; private set; }

        public Task<Role?> FindByIdAsync(long id, CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult<Role?>(Role.Id == id ? Role : null);
        }

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken)
        {
            NameExistsCalls++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(NameExists);
        }

        public Task<bool> HasActiveAssignmentsAsync(long roleId, CancellationToken cancellationToken)
        {
            ActiveAssignmentChecks++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(HasActiveAssignments);
        }

        public Task<RoleReadModel> CreateAsync(
            Role role,
            RoleGovernanceAudit audit,
            CancellationToken cancellationToken)
        {
            CreatedRole = role;
            LastAudit = audit;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(ToReadModel(role));
        }

        public Task<RoleReadModel> UpdateAsync(
            Role role,
            RoleGovernanceAudit audit,
            CancellationToken cancellationToken)
        {
            UpdateCount++;
            LastAudit = audit;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(ToReadModel(role));
        }

        public Task<bool> DeleteAsync(
            Role role,
            RoleGovernanceAudit audit,
            CancellationToken cancellationToken)
        {
            DeleteCount++;
            LastAudit = audit;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(true);
        }

        private static RoleReadModel ToReadModel(Role role) =>
            new(
                role.Id,
                role.Name,
                JsonSerializer.Deserialize<List<string>>(role.Permissions) ?? [],
                role.Users.Count(user => user.IsActive),
                role.CreatedAt,
                role.UpdatedAt);
    }
}
