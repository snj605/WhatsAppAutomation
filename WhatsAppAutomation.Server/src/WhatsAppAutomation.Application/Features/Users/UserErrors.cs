using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Users;

public static class UserErrors
{
    public static readonly Error NotFound = new("users.not_found", "This user does not exist.", ErrorKind.NotFound);

    public static readonly Error UserNameTaken =
        new("users.username_taken", "Another user already has this user name.", ErrorKind.Conflict);

    public static readonly Error UnknownRole =
        new("users.unknown_role", "One of the chosen roles does not exist.", ErrorKind.Validation);

    public static readonly Error LastOwner =
        new("users.last_owner", "This is the last active Owner. Make someone else an Owner first.", ErrorKind.Conflict);

    public static readonly Error CannotDisableSelf =
        new("users.cannot_disable_self", "You cannot disable your own account.", ErrorKind.Conflict);

    public static readonly Error CannotDeleteSelf =
        new("users.cannot_delete_self", "You cannot delete your own account.", ErrorKind.Conflict);
}
