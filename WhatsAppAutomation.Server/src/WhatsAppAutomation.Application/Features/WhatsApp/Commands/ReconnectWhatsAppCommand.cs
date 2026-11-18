using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.WhatsApp.Commands;

/// <summary>
/// Connects the signed-in user's number again with its saved login, without scanning a new QR: for a number that
/// stopped (for example because another copy of the gateway took it) or is waiting to reconnect. The new status
/// arrives as usual from the gateway.
/// </summary>
public sealed record ReconnectWhatsAppCommand : ICommand<WhatsAppAccountDto>;

internal sealed class ReconnectWhatsAppCommandHandler(IAppDbContext db, ICurrentUser currentUser, IWhatsAppGateway gateway)
    : ICommandHandler<ReconnectWhatsAppCommand, WhatsAppAccountDto>
{
    public async Task<Result<WhatsAppAccountDto>> Handle(ReconnectWhatsAppCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return WhatsAppErrors.NotSignedIn;
        }

        var account = await db.WhatsAppAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
        if (account is null || account.Status is WhatsAppConnectionStatus.NotLinked or WhatsAppConnectionStatus.LoggedOut)
        {
            return WhatsAppErrors.NoSavedLogin;
        }

        var reconnected = await gateway.ReconnectAsync(account.Id, cancellationToken);
        return reconnected.Error is { } error ? error : WhatsAppAccountDto.From(account);
    }
}
