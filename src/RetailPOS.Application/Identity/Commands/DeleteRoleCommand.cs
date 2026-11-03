using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Identity.Abstractions;

namespace RetailPOS.Application.Identity.Commands;

public sealed record DeleteRoleCommand(long Id) : ICommand<bool>;

public sealed class DeleteRoleCommandHandler(IRoleGovernanceStore store)
    : IRequestHandler<DeleteRoleCommand, bool>
{
    public async Task<bool> Handle(
        DeleteRoleCommand request,
        CancellationToken cancellationToken)
    {
        var role = await store.FindByIdAsync(request.Id, cancellationToken);
        if (role is null)
        {
            throw new KeyNotFoundException($"Role with ID {request.Id} not found");
        }

        if (CreateRoleCommandHandler.IsSuperAdminRole(role.Name))
        {
            throw new InvalidOperationException("Super Admin role cannot be deleted.");
        }

        if (await store.HasActiveAssignmentsAsync(request.Id, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Cannot delete role '{role.Name}' because it is assigned to users");
        }

        return await store.DeleteAsync(
            role,
            new RoleGovernanceAudit(
                RoleGovernanceAction.Delete,
                OldName: role.Name,
                OldPermissions: role.Permissions,
                NewName: null,
                NewPermissions: null),
            cancellationToken);
    }
}
