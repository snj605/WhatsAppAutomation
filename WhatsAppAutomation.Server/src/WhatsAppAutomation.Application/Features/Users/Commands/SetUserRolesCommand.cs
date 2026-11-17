using FluentValidation;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Users.Commands;

/// <summary>Replace all of a user's roles.</summary>
public sealed record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : ICommand;

internal sealed class SetUserRolesCommandValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesCommandValidator()
    {
        RuleFor(c => c.RoleIds).NotEmpty().WithMessage("Choose at least one role.");
    }
}

internal sealed class SetUserRolesCommandHandler(IIdentityService identity, IRoleService roles, IPermissionService permissions)
    : ICommandHandler<SetUserRolesCommand>
{
    public async Task<Result> Handle(SetUserRolesCommand command, CancellationToken cancellationToken)
    {
        var user = await identity.FindUserAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var roleIds = command.RoleIds.Distinct().ToList();
        var newRoles = await roles.FindManyAsync(roleIds, cancellationToken);
        if (newRoles.Count != roleIds.Count)
        {
            return UserErrors.UnknownRole;
        }

        var losesOwner = UserPolicies.IsOwner(user) && !newRoles.Any(r => r.IsSystem);
        if (losesOwner && user.IsActive && await identity.CountActiveOwnersAsync(user.Id, cancellationToken) == 0)
        {
            return UserErrors.LastOwner;
        }

        await identity.SetRolesAsync(user.Id, roleIds, cancellationToken);
        permissions.InvalidateAll();
        return Result.Success();
    }
}
