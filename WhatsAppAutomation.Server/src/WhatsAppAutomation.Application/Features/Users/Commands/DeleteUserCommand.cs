using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Users.Commands;

/// <summary>
/// Deletes a user for good, with everything that is theirs: contacts, groups, templates, messages and their
/// linked WhatsApp number (logged out first). Cannot be undone; turning the user off is the reversible choice.
/// </summary>
public sealed record DeleteUserCommand(Guid UserId) : ICommand;

internal sealed class DeleteUserCommandHandler(
    IIdentityService identity,
    ICurrentUser currentUser,
    IAppDbContext db,
    IWhatsAppGateway gateway,
    IPermissionService permissions,
    ILogger<DeleteUserCommandHandler> logger) : ICommandHandler<DeleteUserCommand>
{
    public async Task<Result> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        var user = await identity.FindUserAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.Id == currentUser.UserId)
        {
            return UserErrors.CannotDeleteSelf;
        }

        if (UserPolicies.IsOwner(user) && await identity.CountActiveOwnersAsync(user.Id, cancellationToken) == 0)
        {
            return UserErrors.LastOwner;
        }

        // Log the number out on WhatsApp first, so the gateway stops using it. If the gateway is down the saved
        // login is still deleted with the user, and the number shows up as a removed device on the phone.
        var account = await db.WhatsAppAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user.Id, cancellationToken);
        if (account is not null && account.Status != WhatsAppConnectionStatus.NotLinked)
        {
            var unlinked = await gateway.UnlinkAsync(account.Id, cancellationToken);
            if (unlinked.Error is { } error)
            {
                logger.LogWarning("Could not log out the WhatsApp number of deleted user {UserId}: {Code}", user.Id, error.Code);
            }
        }

        await identity.DeleteUserAsync(user.Id, cancellationToken);
        permissions.InvalidateAll();
        return Result.Success();
    }
}
