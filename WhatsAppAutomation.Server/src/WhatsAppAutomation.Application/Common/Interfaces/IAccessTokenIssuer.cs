namespace WhatsAppAutomation.Application.Common.Interfaces;

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

public interface IAccessTokenIssuer
{
    /// <summary>
    /// Creates a short-lived JWT. It says who the user is, not what they may do: permissions are
    /// looked up on each request, so a change to a role takes effect at once.
    /// </summary>
    IssuedAccessToken Issue(UserSummary user, string deviceId);
}
