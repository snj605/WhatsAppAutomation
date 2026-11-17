using FluentValidation;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Roles.Commands;

/// <summary>Create a role and choose what it may do.</summary>
public sealed record CreateRoleCommand(string Name, string? Description, IReadOnlyList<string> Permissions) : ICommand<Guid>;

internal sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator() => RoleRules.Apply(this, c => c.Name, c => c.Description, c => c.Permissions);
}

internal sealed class CreateRoleCommandHandler(IRoleService roles, IPermissionService permissions)
    : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();
        if (await roles.NameExistsAsync(name, null, cancellationToken))
        {
            return RoleErrors.NameTaken;
        }

        var id = await roles.CreateAsync(
            name, RoleRules.CleanDescription(command.Description), [.. command.Permissions.Distinct()], cancellationToken);
        permissions.InvalidateAll();
        return id;
    }
}
