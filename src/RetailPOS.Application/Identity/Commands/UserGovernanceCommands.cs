using FluentValidation;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Identity.Commands;

public sealed record CreateUserCommand(
    UserGovernanceProfile Profile,
    string? Password) : ICommand<UserReadModel>;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.Profile.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters");
        RuleFor(command => command.Profile.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(100).WithMessage("Email cannot exceed 100 characters");
        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters")
            .MaximumLength(100).WithMessage("Password cannot exceed 100 characters");
        RuleFor(command => command.Profile.DefaultLocationType)
            .MaximumLength(20);
        RuleFor(command => command.Profile.InventoryLocationAccessScope)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(40);
    }
}

public sealed class CreateUserCommandHandler(
    IUserGovernanceStore store,
    IUserGovernanceContext context)
    : IRequestHandler<CreateUserCommand, UserReadModel>
{
    public Task<UserReadModel> Handle(CreateUserCommand request, CancellationToken cancellationToken) =>
        store.ExecuteSerializableAsync(async transactionToken =>
        {
            var actor = await UserGovernanceCommandHandlerHelpers.GetActorAsync(store, context, transactionToken);
            var email = request.Profile.Email ?? throw new InvalidOperationException("Email is required");
            if (await store.EmailExistsAsync(email, null, transactionToken))
            {
                throw new InvalidOperationException($"User with email '{email}' already exists");
            }

            var role = await store.FindRoleAsync(request.Profile.RoleId, transactionToken)
                ?? throw new KeyNotFoundException($"Role with ID {request.Profile.RoleId} not found");

            UserGovernanceCommandHandlerHelpers.EnsureCanAssignRole(actor, role);

            long? businessId;
            if (UserGovernanceCommandHandlerHelpers.IsSuperAdmin(actor.RoleName))
            {
                businessId = request.Profile.BusinessId;
                if (!businessId.HasValue)
                {
                    throw new InvalidOperationException("SuperAdmin must specify BusinessId when creating tenant users.");
                }
            }
            else
            {
                businessId = actor.BusinessId;
                if (!businessId.HasValue)
                {
                    throw new UnauthorizedAccessException("A persisted business scope is required to create users.");
                }
            }

            return await store.CreateAsync(
                request.Profile,
                request.Password ?? throw new InvalidOperationException("Password is required"),
                businessId,
                actor.Id,
                transactionToken);
        }, cancellationToken);
}

public sealed record UpdateUserCommand(long Id, UserGovernanceProfile Profile) : ICommand<UserReadModel>;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(command => command.Profile.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters");
        RuleFor(command => command.Profile.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(100).WithMessage("Email cannot exceed 100 characters");
        RuleFor(command => command.Profile.DefaultLocationType)
            .MaximumLength(20);
        RuleFor(command => command.Profile.InventoryLocationAccessScope)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(40);
    }
}

public sealed class UpdateUserCommandHandler(
    IUserGovernanceStore store,
    IUserGovernanceContext context)
    : IRequestHandler<UpdateUserCommand, UserReadModel>
{
    public Task<UserReadModel> Handle(UpdateUserCommand request, CancellationToken cancellationToken) =>
        store.ExecuteSerializableAsync(async transactionToken =>
        {
            var actor = await UserGovernanceCommandHandlerHelpers.GetActorAsync(store, context, transactionToken);
            var target = await store.FindTargetAsync(
                request.Id,
                UserGovernanceCommandHandlerHelpers.IsSuperAdmin(actor.RoleName) ? null : actor.BusinessId,
                transactionToken)
                ?? throw new KeyNotFoundException($"User with ID {request.Id} not found");
            var email = request.Profile.Email ?? throw new InvalidOperationException("Email is required");
            if (target.Email != email && await store.EmailExistsAsync(email, request.Id, transactionToken))
            {
                throw new InvalidOperationException($"User with email '{email}' already exists");
            }

            var role = await store.FindRoleAsync(request.Profile.RoleId, transactionToken)
                ?? throw new KeyNotFoundException($"Role with ID {request.Profile.RoleId} not found");
            var actorIsSuperAdmin = UserGovernanceCommandHandlerHelpers.IsSuperAdmin(actor.RoleName);
            UserGovernanceCommandHandlerHelpers.EnsureCanMutateTarget(actor, target, actorIsSuperAdmin);
            UserGovernanceCommandHandlerHelpers.EnsureCanAssignRole(actor, role);
            if (actor.Id == target.Id && !request.Profile.IsActive)
            {
                throw new UnauthorizedAccessException("You cannot deactivate your own user account.");
            }

            var targetIsSuperAdmin = UserGovernanceCommandHandlerHelpers.IsSuperAdmin(target.RoleName);
            var remainsSuperAdmin = UserGovernanceCommandHandlerHelpers.IsSuperAdmin(role.Name)
                && request.Profile.IsActive;
            if (target.IsActive && targetIsSuperAdmin && !remainsSuperAdmin
                && await store.CountActiveSuperAdminsAsync(transactionToken) <= 1)
            {
                throw new InvalidOperationException("At least one active Super Admin must remain.");
            }

            return await store.UpdateAsync(
                request.Id,
                request.Profile,
                actorIsSuperAdmin,
                actor.Id,
                transactionToken);
        }, cancellationToken);
}

public sealed record DeleteUserCommand(long Id) : ICommand<bool>;

public sealed class DeleteUserCommandHandler(
    IUserGovernanceStore store,
    IUserGovernanceContext context)
    : IRequestHandler<DeleteUserCommand, bool>
{
    public Task<bool> Handle(DeleteUserCommand request, CancellationToken cancellationToken) =>
        store.ExecuteSerializableAsync(async transactionToken =>
        {
            var actor = await UserGovernanceCommandHandlerHelpers.GetActorAsync(store, context, transactionToken);
            var actorIsSuperAdmin = UserGovernanceCommandHandlerHelpers.IsSuperAdmin(actor.RoleName);
            var target = await store.FindTargetAsync(
                request.Id,
                actorIsSuperAdmin ? null : actor.BusinessId,
                transactionToken)
                ?? throw new KeyNotFoundException($"User with ID {request.Id} not found");
            UserGovernanceCommandHandlerHelpers.EnsureCanMutateTarget(actor, target, actorIsSuperAdmin);
            if (actor.Id == target.Id)
            {
                throw new UnauthorizedAccessException("You cannot delete your own user account.");
            }

            if (target.IsActive
                && UserGovernanceCommandHandlerHelpers.IsSuperAdmin(target.RoleName)
                && await store.CountActiveSuperAdminsAsync(transactionToken) <= 1)
            {
                throw new InvalidOperationException("At least one active Super Admin must remain.");
            }

            return await store.DeleteAsync(
                request.Id,
                actorIsSuperAdmin ? null : actor.BusinessId,
                actor.Id,
                transactionToken);
        }, cancellationToken);
}

public sealed record SetUserActiveCommand(long Id, bool IsActive) : ICommand<bool>;

public sealed class SetUserActiveCommandHandler(
    IUserGovernanceStore store,
    IUserGovernanceContext context)
    : IRequestHandler<SetUserActiveCommand, bool>
{
    public Task<bool> Handle(SetUserActiveCommand request, CancellationToken cancellationToken) =>
        store.ExecuteSerializableAsync(async transactionToken =>
        {
            var actor = await UserGovernanceCommandHandlerHelpers.GetActorAsync(store, context, transactionToken);
            var actorIsSuperAdmin = UserGovernanceCommandHandlerHelpers.IsSuperAdmin(actor.RoleName);
            var target = await store.FindTargetAsync(
                request.Id,
                actorIsSuperAdmin ? null : actor.BusinessId,
                transactionToken)
                ?? throw new KeyNotFoundException($"User with ID {request.Id} not found");
            UserGovernanceCommandHandlerHelpers.EnsureCanMutateTarget(actor, target, actorIsSuperAdmin);
            if (actor.Id == target.Id && !request.IsActive)
            {
                throw new UnauthorizedAccessException("You cannot deactivate your own user account.");
            }

            if (!request.IsActive && target.IsActive
                && UserGovernanceCommandHandlerHelpers.IsSuperAdmin(target.RoleName)
                && await store.CountActiveSuperAdminsAsync(transactionToken) <= 1)
            {
                throw new InvalidOperationException("At least one active Super Admin must remain.");
            }

            return await store.SetActiveAsync(
                request.Id,
                request.IsActive,
                actorIsSuperAdmin ? null : actor.BusinessId,
                actor.Id,
                transactionToken);
        }, cancellationToken);
}

internal static class UserGovernanceCommandHandlerHelpers
{
    public static async Task<UserGovernanceActor> GetActorAsync(
        IUserGovernanceStore store,
        IUserGovernanceContext context,
        CancellationToken cancellationToken)
    {
        if (context.RealUserId is not long realUserId)
        {
            throw new UnauthorizedAccessException("Unable to determine the authenticated user.");
        }

        var actor = await store.FindActorAsync(realUserId, cancellationToken);
        if (actor is null || !actor.IsActive)
        {
            throw new UnauthorizedAccessException("The authenticated user is not an active persisted account.");
        }

        if (!IsSuperAdmin(actor.RoleName) && !actor.BusinessId.HasValue)
        {
            throw new UnauthorizedAccessException("A persisted business scope is required to manage users.");
        }

        return actor;
    }

    public static void EnsureCanAssignRole(UserGovernanceActor actor, UserGovernanceRole role)
    {
        if (IsSuperAdmin(role.Name) && !IsSuperAdmin(actor.RoleName))
        {
            throw new UnauthorizedAccessException("Only a real Super Admin can assign the Super Admin role.");
        }
    }

    public static void EnsureCanMutateTarget(
        UserGovernanceActor actor,
        UserGovernanceTarget target,
        bool actorIsSuperAdmin)
    {
        if (IsSuperAdmin(target.RoleName) && !actorIsSuperAdmin)
        {
            throw new UnauthorizedAccessException("Only a real Super Admin can manage a Super Admin user.");
        }
    }

    public static bool IsSuperAdmin(string? roleName) =>
        string.Equals(roleName, SystemRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase);
}
