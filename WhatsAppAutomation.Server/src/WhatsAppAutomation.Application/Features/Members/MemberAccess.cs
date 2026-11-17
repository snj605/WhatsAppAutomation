using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Members;

/// <summary>
/// Who may manage whom. A tenant admin manages the people of their own tenant; the platform admin manages
/// the tenant admins of every tenant, never members (their contacts and messages are the tenant's business).
/// Nobody manages their own account here: they change their own password on the account screen.
/// </summary>
internal sealed class MemberAccess(ICurrentUser currentUser, IIdentityService identity)
{
    /// <summary>The signed-in user, or null when the session no longer belongs to anyone.</summary>
    public async Task<UserDetails?> CallerAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } id ? await identity.FindUserAsync(id, cancellationToken) : null;

    /// <summary>
    /// The caller and the target user, if the caller may manage them. A user out of reach is reported as
    /// "not found", so nobody learns about other tenants' people.
    /// </summary>
    public async Task<Result<(UserDetails Caller, UserDetails Target)>> TargetAsync(Guid targetId, CancellationToken cancellationToken)
    {
        if (await CallerAsync(cancellationToken) is not { } caller)
        {
            return MemberErrors.NotFound;
        }

        if (caller.Id == targetId)
        {
            return MemberErrors.NotYourself;
        }

        if (await identity.FindUserAsync(targetId, cancellationToken) is not { } target || !MayManage(caller, target))
        {
            return MemberErrors.NotFound;
        }

        return Result<(UserDetails Caller, UserDetails Target)>.Success((caller, target));
    }

    public static bool MayManage(UserDetails caller, UserDetails target) => caller.Kind switch
    {
        UserKind.PlatformAdmin => target.Kind == UserKind.TenantAdmin,
        UserKind.TenantAdmin => target.TenantId == caller.TenantId && target.Kind != UserKind.PlatformAdmin,
        _ => false,
    };

    /// <summary>Whether taking this admin away (turning off, deleting, or making a member) leaves the tenant without one.</summary>
    public async Task<bool> IsLastAdminAsync(UserDetails target, CancellationToken cancellationToken) =>
        target.Kind == UserKind.TenantAdmin && target.IsActive &&
        await identity.CountActiveAsync(UserKind.TenantAdmin, target.TenantId, target.Id, cancellationToken) == 0;
}
