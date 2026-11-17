using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>The signed-in user as tokens and sessions need it.</summary>
public sealed record UserSummary(Guid Id, string UserName, string DisplayName, IReadOnlyList<string> Roles);

public sealed record RoleRef(Guid Id, string Name);

/// <summary>A user as the user management screen shows it.</summary>
public sealed record UserDetails(
    Guid Id,
    string UserName,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<RoleRef> Roles,
    DateTimeOffset CreatedAt);

public sealed record NewUser(string UserName, string DisplayName, string Password, IReadOnlyCollection<Guid> RoleIds);

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
    /// Checks a password and applies lockout on failure. Unknown users and wrong passwords
    /// give the same outcome so a caller cannot tell which one it was.
    /// </summary>
    Task<PasswordCheckResult> CheckPasswordAsync(string userName, string password, CancellationToken cancellationToken);

    /// <summary>Returns the user if it exists and is allowed to sign in, otherwise null.</summary>
    Task<UserSummary?> FindActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserDetails>> ListUsersAsync(CancellationToken cancellationToken);

    Task<UserDetails?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken);

    /// <summary>Creates the user. Fails with a validation error when the password breaks the password rules.</summary>
    Task<Result<Guid>> CreateUserAsync(NewUser user, CancellationToken cancellationToken);

    /// <summary>Replaces all of the user's roles with these.</summary>
    Task SetRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>Deletes the user; everything that belongs to them goes with them (the database cascades).</summary>
    Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Active users holding the Owner role, optionally not counting one user.</summary>
    Task<int> CountActiveOwnersAsync(Guid? excludingUserId, CancellationToken cancellationToken);
}
