using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.WhatsApp.Queries;

/// <summary>The signed-in user's most recent messages, newest first.</summary>
public sealed record GetMyMessagesQuery(int Take = 20) : IQuery<IReadOnlyList<OutgoingMessageDto>>;

internal sealed class GetMyMessagesQueryValidator : AbstractValidator<GetMyMessagesQuery>
{
    public GetMyMessagesQueryValidator() => RuleFor(q => q.Take).InclusiveBetween(1, 100);
}

internal sealed class GetMyMessagesQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyMessagesQuery, IReadOnlyList<OutgoingMessageDto>>
{
    public async Task<Result<IReadOnlyList<OutgoingMessageDto>>> Handle(GetMyMessagesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return WhatsAppErrors.NotSignedIn;
        }

        var messages = await db.OutgoingMessages.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(query.Take)
            .ToListAsync(cancellationToken);
        return messages.Select(OutgoingMessageDto.From).ToList();
    }
}
