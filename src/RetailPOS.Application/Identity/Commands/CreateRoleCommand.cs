using System.Text.Json;
using FluentValidation;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Identity.Commands;

public sealed record CreateRoleCommand(
    string? Name,
    IReadOnlyList<string?>? Permissions) : ICommand<RoleReadModel>;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Role name is required")
            .MaximumLength(50)
            .WithMessage("Role name cannot exceed 50 characters");

        RuleFor(command => command.Permissions)
            .NotNull()
            .WithMessage("Permissions are required");
    }
}

public sealed class CreateRoleCommandHandler(
    IRoleGovernanceStore store,
    IRoleGovernanceContext context)
    : IRequestHandler<CreateRoleCommand, RoleReadModel>
{
    public async Task<RoleReadModel> Handle(
        CreateRoleCommand request,
        CancellationToken cancellationToken)
    {
        var name = request.Name ?? throw new InvalidOperationException("Role name is required");
        if (IsSuperAdminRole(name) && !context.IsSuperAdmin)
        {
            throw new UnauthorizedAccessException("Only Super Admin can manage this role.");
        }

        if (await store.NameExistsAsync(name, cancellationToken))
        {
            throw new InvalidOperationException($"Role with name '{name}' already exists");
        }

        var permissions = RolePermissionNormalizer.Normalize(request.Permissions);
        ValidatePermissions(permissions);

        var role = new Role
        {
            Name = name,
            Permissions = JsonSerializer.Serialize(permissions)
        };
        var audit = new RoleGovernanceAudit(
            RoleGovernanceAction.Create,
            OldName: null,
            OldPermissions: null,
            NewName: role.Name,
            NewPermissions: role.Permissions);

        return await store.CreateAsync(role, audit, cancellationToken);
    }

    internal static void ValidatePermissions(IReadOnlyList<string> permissions)
    {
        var invalidPermissions = permissions
            .Where(permission => permission != "*" && !PermissionCatalog.All.Contains(permission))
            .ToArray();
        if (invalidPermissions.Length > 0)
        {
            throw new InvalidOperationException(
                $"Invalid permissions: {string.Join(", ", invalidPermissions)}");
        }
    }

    internal static bool IsSuperAdminRole(string? roleName) =>
        string.Equals(roleName, "Super Admin", StringComparison.OrdinalIgnoreCase);
}
