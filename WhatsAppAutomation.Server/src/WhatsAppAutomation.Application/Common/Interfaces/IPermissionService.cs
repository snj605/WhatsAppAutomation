namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>Answers "what may this user do?" for authorization checks.</summary>
public interface IPermissionService
{
    /// <summary>
    /// The user's permissions, from their kind and (for members) their switches. Empty for an unknown or
    /// disabled user, a user of a turned-off tenant, or one who must first choose their own password. Cached.
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Forgets cached permissions. Call after any change to a user's kind, switches, activity or tenant.</summary>
    void InvalidateAll();
}

/// <summary>Who is making the current request.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}
