using FluentValidation;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Roles.Commands;

/// <summary>Rename a role, change its description, and replace its permissions.</summary>
public sealed record UpdateRoleCommand(Guid RoleId, string Name, string? Description, IReadOnlyList<string> Permissions) : ICommand;

internal sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator() => RoleRules.Apply(this, c => c.Name, c => c.Description, c => c.Permissions);
}

internal sealed class UpdateRoleCommandHandler(IRoleService roles, IPermissionService permissions)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.FindAsync(command.RoleId, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystem)
        {
            return RoleErrors.SystemRole;
        }

        var name = command.Name.Trim();
        if (await roles.NameExistsAsync(name, role.Id, cancellationToken))
        {
            return RoleErrors.NameTaken;
        }

        await roles.UpdateAsync(
            role.Id, name, RoleRules.CleanDescription(command.Description), [.. command.Permissions.Distinct()], cancellationToken);
        permissions.InvalidateAll();
        return Result.Success();
    }
}
