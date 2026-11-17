using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Members;

/// <summary>Logs linked WhatsApp numbers out before their users are deleted, so the gateway stops using them.</summary>
internal sealed class UserRemoval(IAppDbContext db, IWhatsAppGateway gateway, ILogger<UserRemoval> logger)
{
    /// <summary>
    /// Unlinks the numbers of these users. If the gateway is down the saved login is still deleted with the
    /// user (the database cascades), and the number shows up as a removed device on the phone.
    /// </summary>
    public async Task UnlinkNumbersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var accounts = await db.WhatsAppAccounts.AsNoTracking()
            .Where(a => userIds.Contains(a.UserId) && a.Status != WhatsAppConnectionStatus.NotLinked)
            .Select(a => new { a.Id, a.UserId })
            .ToListAsync(cancellationToken);

        foreach (var account in accounts)
        {
            var unlinked = await gateway.UnlinkAsync(account.Id, cancellationToken);
            if (unlinked.Error is { } error)
            {
                logger.LogWarning("Could not log out the WhatsApp number of deleted user {UserId}: {Code}", account.UserId, error.Code);
            }
        }
    }
}
