using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Roles.Commands;

public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;

internal sealed class DeleteRoleCommandHandler(IRoleService roles, IPermissionService permissions)
    : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
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

        if (role.UserCount > 0)
        {
            return RoleErrors.InUse;
        }

        await roles.DeleteAsync(role.Id, cancellationToken);
        permissions.InvalidateAll();
        return Result.Success();
    }
}
