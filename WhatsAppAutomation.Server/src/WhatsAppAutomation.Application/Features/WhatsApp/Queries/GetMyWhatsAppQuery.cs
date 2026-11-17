using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.WhatsApp.Queries;

/// <summary>The signed-in user's WhatsApp link: status and linked number.</summary>
public sealed record GetMyWhatsAppQuery : IQuery<WhatsAppAccountDto>;

internal sealed class GetMyWhatsAppQueryHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IQueryHandler<GetMyWhatsAppQuery, WhatsAppAccountDto>
{
    public async Task<Result<WhatsAppAccountDto>> Handle(GetMyWhatsAppQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return WhatsAppErrors.NotSignedIn;
        }

        var account = await db.WhatsAppAccounts.SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);

        // A linking the gateway never finished (e.g. it restarted) would otherwise wait for a QR code forever.
        if (account is not null && account.ExpireStaleLinking(clock.GetUtcNow()))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return WhatsAppAccountDto.From(account);
    }
}
