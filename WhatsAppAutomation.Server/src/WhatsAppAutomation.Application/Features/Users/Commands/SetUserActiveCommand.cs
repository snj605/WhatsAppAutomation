using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Authentication;

namespace WhatsAppAutomation.Application.Features.Users.Commands;

/// <summary>Enable or disable a user. Disabling signs them out on every device.</summary>
public sealed record SetUserActiveCommand(Guid UserId, bool IsActive) : ICommand;

internal sealed class SetUserActiveCommandHandler(
    IIdentityService identity,
    ICurrentUser currentUser,
    SessionStore sessions,
    IPermissionService permissions,
    TimeProvider clock) : ICommandHandler<SetUserActiveCommand>
{
    public async Task<Result> Handle(SetUserActiveCommand command, CancellationToken cancellationToken)
    {
        var user = await identity.FindUserAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (!command.IsActive)
        {
            if (user.Id == currentUser.UserId)
            {
                return UserErrors.CannotDisableSelf;
            }

            if (UserPolicies.IsOwner(user) && await identity.CountActiveOwnersAsync(user.Id, cancellationToken) == 0)
            {
                return UserErrors.LastOwner;
            }
        }

        await identity.SetActiveAsync(user.Id, command.IsActive, cancellationToken);
        if (!command.IsActive)
        {
            await sessions.RevokeUserAsync(user.Id, clock.GetUtcNow(), cancellationToken);
        }

        permissions.InvalidateAll();
        return Result.Success();
    }
}
