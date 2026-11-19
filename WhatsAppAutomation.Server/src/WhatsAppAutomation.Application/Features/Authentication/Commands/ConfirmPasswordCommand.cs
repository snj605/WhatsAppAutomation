using FluentValidation;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Authentication.Commands;

/// <summary>
/// The signed-in user types their password again before changing an important setting (sending hours, an
/// occasion's automatic sending, deleting), so nothing changes by mistake. Only checks it: nothing is stored.
/// Wrong tries count towards the same lockout as signing in.
/// </summary>
public sealed record ConfirmPasswordCommand(string Password) : ICommand;

internal sealed class ConfirmPasswordCommandValidator : AbstractValidator<ConfirmPasswordCommand>
{
    public ConfirmPasswordCommandValidator() => RuleFor(c => c.Password).NotEmpty().MaximumLength(1024);
}

internal sealed class ConfirmPasswordCommandHandler(ICurrentUser currentUser, IIdentityService identity)
    : ICommandHandler<ConfirmPasswordCommand>
{
    public async Task<Result> Handle(ConfirmPasswordCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await identity.FindUserAsync(userId, cancellationToken) is not { } user)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var check = await identity.CheckPasswordAsync(user.UserName, command.Password, cancellationToken);
        return check.Outcome switch
        {
            PasswordCheckOutcome.Succeeded when check.User?.Id == userId => Result.Success(),
            PasswordCheckOutcome.LockedOut => AuthErrors.LockedOut,
            PasswordCheckOutcome.Disabled => AuthErrors.Disabled,
            _ => AuthErrors.WrongPassword,
        };
    }
}
