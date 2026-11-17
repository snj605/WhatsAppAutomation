using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

/// <summary>
/// Every member may send a single message (one contact). Sending to several people, groups, categories or
/// everyone needs the Broadcasts switch. The endpoints require messages.send; this adds the second check.
/// </summary>
internal static class BroadcastAccess
{
    public static async Task<bool> MaySendAsync(
        IPermissionService permissions, Guid userId, BroadcastInput input, CancellationToken cancellationToken) =>
        input.IsSingleMessage ||
        (await permissions.GetPermissionsAsync(userId, cancellationToken)).Contains(Permissions.Broadcasts.Send);
}
