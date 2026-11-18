using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>The signed-in user as tokens, sessions and the apps need it.</summary>
public sealed record UserSummary(
    Guid Id, string UserName, string DisplayName, UserKind Kind, Guid? TenantId, string? TenantName, bool MustChangePassword);

/// <summary>A user as the member and tenant screens show them.</summary>
/// <param name="Switches">For members: the switches their tenant admin turned on.</param>
public sealed record UserDetails(
    Guid Id,
    string UserName,
    string DisplayName,
    UserKind Kind,
    Guid? TenantId,
    bool IsActive,
    IReadOnlyList<string> Switches,
    DateTimeOffset CreatedAt);

public sealed record NewUser(
    string UserName,
    string DisplayName,
    string Password,
    UserKind Kind,
    Guid? TenantId,
    IReadOnlyCollection<string> Switches,
    bool MustChangePassword);

public enum PasswordCheckOutcome
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
    Disabled,
}

public sealed record PasswordCheckResult(PasswordCheckOutcome Outcome, UserSummary? User);

/// <summary>User store operations the application needs. Implemented with ASP.NET Core Identity.</summary>
public interface IIdentityService
{
    /// <summary>
    /// Checks a password and applies lockout on failure. Unknown users and wrong passwords give the same
    /// outcome so a caller cannot tell which one it was. A user of a turned-off tenant counts as disabled.
    /// </summary>
    Task<PasswordCheckResult> CheckPasswordAsync(string userName, string password, CancellationToken cancellationToken);

    /// <summary>Returns the user if it exists and is allowed to sign in (user and tenant active), otherwise null.</summary>
    Task<UserSummary?> FindActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Whether the user's messages may go out now: the user and their tenant are both turned on.</summary>
    Task<bool> MaySendAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The users of one tenant, or the platform admins when tenantId is null, by name.</summary>
    Task<IReadOnlyList<UserDetails>> ListUsersAsync(Guid? tenantId, CancellationToken cancellationToken);

    Task<UserDetails?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken);

    /// <summary>Creates the user. Fails with a validation error when the password breaks the password rules.</summary>
    Task<Result<Guid>> CreateUserAsync(NewUser user, CancellationToken cancellationToken);

    Task UpdateUserAsync(Guid userId, string displayName, UserKind kind, IReadOnlyCollection<string> switches, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>Deletes the user; everything that belongs to them goes with them (the database cascades).</summary>
    Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Active users of this kind in the tenant (null: platform admins), optionally not counting one user.</summary>
    Task<int> CountActiveAsync(UserKind kind, Guid? tenantId, Guid? excludingUserId, CancellationToken cancellationToken);

    /// <summary>Every tenant user (not platform admins): id, tenant and name, for the platform dashboard.</summary>
    Task<IReadOnlyList<(Guid UserId, Guid? TenantId, string DisplayName)>> UsersByTenantAsync(CancellationToken cancellationToken);

    /// <summary>How many users each tenant has, and how many of them are tenant admins.</summary>
    Task<IReadOnlyDictionary<Guid, (int Users, int Admins)>> CountByTenantAsync(CancellationToken cancellationToken);

    /// <summary>Checks the current password, then sets the new one and clears "must change". Validation error if wrong or weak.</summary>
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken);

    /// <summary>Sets a password chosen by an admin; the user must then choose their own at the next sign-in.</summary>
    Task<Result> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken);
}
