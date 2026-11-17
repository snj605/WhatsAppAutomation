using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Users;

internal static class UserPolicies
{
    public static bool IsOwner(UserDetails user) =>
        user.Roles.Any(r => r.Name == SystemRoles.Owner);
}
