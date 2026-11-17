namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>Answers "what may this user do?" for authorization checks.</summary>
public interface IPermissionService
{
    /// <summary>
    /// The user's permissions: the union of their roles' permissions, or every permission for an
    /// Owner. Empty for an unknown or disabled user. Results are cached.
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Forgets cached permissions. Call after any change to roles, role permissions or user roles.</summary>
    void InvalidateAll();
}

/// <summary>Who is making the current request.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}
