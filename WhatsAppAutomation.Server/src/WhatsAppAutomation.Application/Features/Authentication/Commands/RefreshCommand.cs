using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Authentication.Commands;

/// <summary>Swap a refresh token for a new access token and a new refresh token.</summary>
public sealed record RefreshCommand(string RefreshToken, string DeviceId) : ICommand<AuthSession>;

internal sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DeviceId).NotEmpty().MaximumLength(Domain.Entities.RefreshToken.DeviceIdMaxLength);
    }
}

internal sealed partial class RefreshCommandHandler(
    IAppDbContext db,
    IIdentityService identity,
    SessionStore sessions,
    TimeProvider clock,
    ILogger<RefreshCommandHandler> logger) : ICommandHandler<RefreshCommand, AuthSession>
{
    public async Task<Result<AuthSession>> Handle(RefreshCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var hash = RefreshTokenSecret.Hash(command.RefreshToken);
        var current = await db.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (current is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        if (current.RevokedAt is not null || current.DeviceId != command.DeviceId)
        {
            // A used or revoked token came back, or from another device: assume it was stolen.
            await sessions.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            LogTokenReuse(logger, current.UserId, current.FamilyId);
            return AuthErrors.InvalidRefreshToken;
        }

        if (current.ExpiresAt <= now)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var user = await identity.FindActiveUserAsync(current.UserId, cancellationToken);
        if (user is null)
        {
            await sessions.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            return AuthErrors.InvalidRefreshToken;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var replacementId = Guid.NewGuid();

        // Claim the token atomically so two concurrent refreshes cannot both succeed.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.ReplacedById, replacementId),
                cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await sessions.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            LogTokenReuse(logger, current.UserId, current.FamilyId);
            return AuthErrors.InvalidRefreshToken;
        }

        var session = await sessions.IssueAsync(
            user, current.FamilyId, current.DeviceId, current.DeviceName, now, cancellationToken, replacementId);

        await transaction.CommitAsync(cancellationToken);
        return session;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Refresh token reuse for user {UserId}; revoked token family {FamilyId}")]
    private static partial void LogTokenReuse(ILogger logger, Guid userId, Guid familyId);
}
