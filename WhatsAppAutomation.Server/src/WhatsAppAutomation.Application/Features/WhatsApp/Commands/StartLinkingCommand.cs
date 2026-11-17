using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Domain.ValueObjects;

namespace WhatsAppAutomation.Application.Features.WhatsApp.Commands;

/// <summary>
/// Starts linking the signed-in user's WhatsApp number. The QR codes or the phone code then arrive
/// live (SignalR). Linking stops by itself after 3 minutes if nobody completes it.
/// </summary>
/// <param name="PhoneNumber">Only for <see cref="LinkMethod.PhoneCode"/>: the number being linked.</param>
public sealed record StartLinkingCommand(LinkMethod Method, string? PhoneNumber) : ICommand<WhatsAppAccountDto>;

internal sealed class StartLinkingCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IWhatsAppGateway gateway, TimeProvider clock)
    : ICommandHandler<StartLinkingCommand, WhatsAppAccountDto>
{
    public async Task<Result<WhatsAppAccountDto>> Handle(StartLinkingCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return WhatsAppErrors.NotSignedIn;
        }

        string? phone = null;
        if (command.Method == LinkMethod.PhoneCode && (phone = PhoneNumber.Normalize(command.PhoneNumber)) is null)
        {
            return WhatsAppErrors.InvalidPhoneNumber;
        }

        var now = clock.GetUtcNow();
        var account = await db.WhatsAppAccounts.SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
        if (account is null)
        {
            account = new WhatsAppAccount(userId, now);
            db.WhatsAppAccounts.Add(account);

            // The gateway stores the login under this account id, so the row must exist first.
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (account.Status is WhatsAppConnectionStatus.Connected or WhatsAppConnectionStatus.Reconnecting
                 or WhatsAppConnectionStatus.Connecting or WhatsAppConnectionStatus.Restricted)
        {
            return WhatsAppErrors.AlreadyLinked;
        }

        var linked = await gateway.LinkAsync(account.Id, command.Method, phone, cancellationToken);
        if (!linked.IsSuccess)
        {
            return linked.Error!;
        }

        account.StartLinking(now);
        await db.SaveChangesAsync(cancellationToken);
        return WhatsAppAccountDto.From(account);
    }
}
