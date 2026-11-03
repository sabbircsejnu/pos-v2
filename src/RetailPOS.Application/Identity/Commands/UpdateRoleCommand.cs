using System.Text.Json;
using FluentValidation;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Application.Identity.Commands;

public sealed record UpdateRoleCommand(
    long Id,
    string? Name,
    IReadOnlyList<string?>? Permissions) : ICommand<RoleReadModel>;

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
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

public sealed class UpdateRoleCommandHandler(
    IRoleGovernanceStore store,
    IRoleGovernanceContext context)
    : IRequestHandler<UpdateRoleCommand, RoleReadModel>
{
    public async Task<RoleReadModel> Handle(
        UpdateRoleCommand request,
        CancellationToken cancellationToken)
    {
        var role = await store.FindByIdAsync(request.Id, cancellationToken);
        if (role is null)
        {
            throw new KeyNotFoundException($"Role with ID {request.Id} not found");
        }

        var name = request.Name ?? throw new InvalidOperationException("Role name is required");
        if (CreateRoleCommandHandler.IsSuperAdminRole(role.Name) && !context.IsSuperAdmin)
        {
            throw new UnauthorizedAccessException("Only Super Admin can manage this role.");
        }

        if (CreateRoleCommandHandler.IsSuperAdminRole(name) && !context.IsSuperAdmin)
        {
            throw new UnauthorizedAccessException("Only Super Admin can manage this role.");
        }

        if (role.Name != name && await store.NameExistsAsync(name, cancellationToken))
        {
            throw new InvalidOperationException($"Role with name '{name}' already exists");
        }

        var permissions = RolePermissionNormalizer.Normalize(request.Permissions);
        CreateRoleCommandHandler.ValidatePermissions(permissions);

        var oldName = role.Name;
        var oldPermissions = role.Permissions;
        role.Name = name;
        role.Permissions = JsonSerializer.Serialize(permissions);

        return await store.UpdateAsync(
            role,
            new RoleGovernanceAudit(
                RoleGovernanceAction.Update,
                oldName,
                oldPermissions,
                role.Name,
                role.Permissions),
            cancellationToken);
    }
}
