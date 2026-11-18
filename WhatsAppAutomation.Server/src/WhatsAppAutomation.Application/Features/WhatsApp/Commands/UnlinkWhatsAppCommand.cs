using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.WhatsApp.Commands;

/// <summary>
/// Unlinks the signed-in user's number: logs out on WhatsApp (it disappears from Linked devices on
/// the phone) and deletes the saved login. Also cancels a link that is in progress.
/// </summary>
public sealed record UnlinkWhatsAppCommand : ICommand<WhatsAppAccountDto>;

internal sealed class UnlinkWhatsAppCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IWhatsAppGateway gateway, TimeProvider clock)
    : ICommandHandler<UnlinkWhatsAppCommand, WhatsAppAccountDto>
{
    public async Task<Result<WhatsAppAccountDto>> Handle(UnlinkWhatsAppCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return WhatsAppErrors.NotSignedIn;
        }

        var account = await db.WhatsAppAccounts.SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
        if (account is null)
        {
            return WhatsAppAccountDto.NotLinked;
        }

        var unlinked = await gateway.UnlinkAsync(account.Id, cancellationToken);
        if (!unlinked.IsSuccess)
        {
            return unlinked.Error!;
        }

        account.MarkUnlinked(clock.GetUtcNow(), removedFromPhone: unlinked.Value);
        await db.SaveChangesAsync(cancellationToken);
        return WhatsAppAccountDto.From(account);
    }
}
