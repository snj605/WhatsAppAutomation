using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Roles;

public static class RoleErrors
{
    public static readonly Error NotFound = new("roles.not_found", "This role does not exist.", ErrorKind.NotFound);

    public static readonly Error NameTaken =
        new("roles.name_taken", "Another role already has this name.", ErrorKind.Conflict);

    public static readonly Error SystemRole =
        new("roles.system_role", "The Owner role always has every permission and cannot be changed or deleted.", ErrorKind.Conflict);

    public static readonly Error InUse =
        new("roles.in_use", "Users still have this role. Give them another role first.", ErrorKind.Conflict);
}
