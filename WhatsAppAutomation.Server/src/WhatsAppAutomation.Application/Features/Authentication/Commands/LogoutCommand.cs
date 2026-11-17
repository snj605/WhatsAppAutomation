using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Authentication.Commands;

/// <summary>End the session on the device that owns this refresh token. Unknown tokens are ignored.</summary>
public sealed record LogoutCommand(string RefreshToken) : ICommand;

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(200);
    }
}

internal sealed class LogoutCommandHandler(IAppDbContext db, SessionStore sessions, TimeProvider clock)
    : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var hash = RefreshTokenSecret.Hash(command.RefreshToken);
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);

        if (familyId is { } id)
        {
            await sessions.RevokeFamilyAsync(id, clock.GetUtcNow(), cancellationToken);
        }

        return Result.Success();
    }
}
