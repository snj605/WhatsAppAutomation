using FluentValidation;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Members;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Authentication.Commands;

/// <summary>
/// The signed-in user changes their own password. Required after an admin chose it for them: until then the
/// account can do nothing else.
/// </summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand;

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty().MaximumLength(1024);
        RuleFor(c => c.NewPassword).NotEmpty().MaximumLength(PasswordRules.MaxLength)
            .NotEqual(c => c.CurrentPassword).WithMessage("Choose a password different from the current one.");
    }
}

internal sealed class ChangePasswordCommandHandler(ICurrentUser currentUser, IIdentityService identity, IPermissionService permissions)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Result> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await identity.FindUserAsync(userId, cancellationToken) is not { } user)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        if (PasswordRules.Problem(command.NewPassword, user.UserName, user.Kind != UserKind.Member) is { } problem)
        {
            return Error.Validation(problem);
        }

        var changed = await identity.ChangePasswordAsync(userId, command.CurrentPassword, command.NewPassword, cancellationToken);
        if (changed.IsSuccess)
        {
            permissions.InvalidateAll();
        }

        return changed;
    }
}
