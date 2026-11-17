using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Members;

public static class MemberErrors
{
    /// <summary>Also returned for a user outside the caller's reach, so the caller cannot tell they exist.</summary>
    public static readonly Error NotFound = new("users.not_found", "This user does not exist.", ErrorKind.NotFound);

    public static readonly Error UserNameTaken =
        new("users.username_taken", "Another user already has this user name.", ErrorKind.Conflict);

    public static readonly Error LastAdmin = new(
        "users.last_admin", "This is the last active admin of the tenant. Make someone else an admin first.", ErrorKind.Conflict);

    public static readonly Error NotYourself =
        new("users.not_yourself", "You cannot do this to your own account.", ErrorKind.Conflict);

    public static readonly Error UnknownSwitch =
        new("users.unknown_switch", "One of the chosen switches does not exist.", ErrorKind.Validation);
}
