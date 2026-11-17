using FluentValidation;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Users.Commands;

/// <summary>Create a user with a first password and their roles. There is no public sign-up.</summary>
public sealed record CreateUserCommand(string UserName, string DisplayName, string Password, IReadOnlyList<Guid> RoleIds)
    : ICommand<Guid>;

internal sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(c => c.UserName).NotEmpty().MaximumLength(50)
            .Matches("^[a-zA-Z0-9._-]+$").WithMessage("User name may only use letters, digits, '.', '_' and '-'.");
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(1024);
        RuleFor(c => c.RoleIds).NotEmpty().WithMessage("Choose at least one role.");
    }
}

internal sealed class CreateUserCommandHandler(IIdentityService identity, IRoleService roles)
    : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var roleIds = command.RoleIds.Distinct().ToList();
        var chosenRoles = await roles.FindManyAsync(roleIds, cancellationToken);
        if (chosenRoles.Count != roleIds.Count)
        {
            return UserErrors.UnknownRole;
        }

        if (PasswordRules.Problem(command.Password, command.UserName, PasswordRules.ManagesAccounts(chosenRoles)) is { } problem)
        {
            return Error.Validation(problem);
        }

        if (await identity.UserNameExistsAsync(command.UserName, cancellationToken))
        {
            return UserErrors.UserNameTaken;
        }

        return await identity.CreateUserAsync(
            new NewUser(command.UserName.Trim(), command.DisplayName.Trim(), command.Password, roleIds), cancellationToken);
    }
}
