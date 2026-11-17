using FluentValidation;
using Microsoft.Extensions.Logging;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Authentication.Commands;

/// <summary>Sign in with user name and password and start a session on this device.</summary>
public sealed record LoginCommand(string UserName, string Password, string DeviceId, string? DeviceName)
    : ICommand<AuthSession>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.UserName).NotEmpty().MaximumLength(256);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(1024);
        RuleFor(c => c.DeviceId).NotEmpty().MaximumLength(RefreshToken.DeviceIdMaxLength);
        RuleFor(c => c.DeviceName).MaximumLength(RefreshToken.DeviceNameMaxLength);
    }
}

internal sealed partial class LoginCommandHandler(
    IIdentityService identity,
    SessionStore sessions,
    TimeProvider clock,
    ILogger<LoginCommandHandler> logger) : ICommandHandler<LoginCommand, AuthSession>
{
    public async Task<Result<AuthSession>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var check = await identity.CheckPasswordAsync(command.UserName, command.Password, cancellationToken);
        switch (check.Outcome)
        {
            case PasswordCheckOutcome.Succeeded:
                break;
            case PasswordCheckOutcome.LockedOut:
                return AuthErrors.LockedOut;
            case PasswordCheckOutcome.Disabled:
                return AuthErrors.Disabled;
            default:
                return AuthErrors.InvalidCredentials;
        }

        var user = check.User!;
        var now = clock.GetUtcNow();

        // One session per device: signing in again on the same device ends the previous session there.
        await sessions.RevokeDeviceAsync(user.Id, command.DeviceId, now, cancellationToken);

        var session = await sessions.IssueAsync(user, Guid.NewGuid(), command.DeviceId, command.DeviceName, now, cancellationToken);
        LogSignedIn(logger, user.Id);
        return session;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in")]
    private static partial void LogSignedIn(ILogger logger, Guid userId);
}
