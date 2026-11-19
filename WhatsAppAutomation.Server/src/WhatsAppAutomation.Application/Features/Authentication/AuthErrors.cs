using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Authentication;

public static class AuthErrors
{
    public static readonly Error InvalidCredentials =
        new("auth.invalid_credentials", "The username or password is incorrect.", ErrorKind.Unauthorized);

    public static readonly Error LockedOut =
        new("auth.locked_out", "Too many failed attempts. Try again later.", ErrorKind.TooManyRequests);

    public static readonly Error Disabled =
        new("auth.disabled", "This account is disabled.", ErrorKind.Forbidden);

    /// <summary>A wrong password when confirming a change: a plain mistake, not the end of the session.</summary>
    public static readonly Error WrongPassword =
        new("auth.wrong_password", "That password is not correct.", ErrorKind.Validation);

    public static readonly Error InvalidRefreshToken =
        new("auth.invalid_refresh_token", "The session has ended. Sign in again.", ErrorKind.Unauthorized);
}
