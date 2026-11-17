namespace WhatsAppAutomation.Domain.Entities;

/// <summary>
/// A refresh token issued to one device of one user. Only a SHA-256 hash of the token is stored.
/// Tokens rotate: each use revokes the token and issues a new one in the same family.
/// Presenting a revoked token is treated as theft and revokes the whole family.
/// </summary>
public sealed class RefreshToken
{
    public const int DeviceIdMaxLength = 100;
    public const int DeviceNameMaxLength = 100;

    private RefreshToken()
    {
        TokenHash = string.Empty;
        DeviceId = string.Empty;
    }

    public RefreshToken(
        Guid userId,
        string tokenHash,
        Guid familyId,
        string deviceId,
        string? deviceName,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("Expiry must be after creation.", nameof(expiresAt));
        }

        Id = id ?? Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        DeviceId = deviceId;
        DeviceName = deviceName;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    /// <summary>All tokens descended from one login share a family.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>Stable id the app generates once per install and sends on login and refresh.</summary>
    public string DeviceId { get; private set; }

    public string? DeviceName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}
