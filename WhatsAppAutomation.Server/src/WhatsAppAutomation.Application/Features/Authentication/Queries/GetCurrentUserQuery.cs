using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Authentication.Queries;

/// <summary>Who is signed in and what they may do. Apps use the permissions to show or hide screens.</summary>
public sealed record GetCurrentUserQuery : IQuery<CurrentUserResult>;

public sealed record CurrentUserResult(UserSummary User, IReadOnlyList<string> Permissions);

internal sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IIdentityService identity,
    IPermissionService permissions) : IQueryHandler<GetCurrentUserQuery, CurrentUserResult>
{
    public async Task<Result<CurrentUserResult>> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId
            || await identity.FindActiveUserAsync(userId, cancellationToken) is not { } user)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var granted = await permissions.GetPermissionsAsync(userId, cancellationToken);
        return new CurrentUserResult(user, [.. granted.Order(StringComparer.Ordinal)]);
    }
}
