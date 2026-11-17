namespace WhatsAppAutomation.Domain.Entities;

/// <summary>Where a user's WhatsApp link stands. The gateway reports changes as they happen.</summary>
public enum WhatsAppConnectionStatus
{
    /// <summary>No number linked.</summary>
    NotLinked,

    /// <summary>Waiting for the QR to be scanned or the phone code to be entered.</summary>
    Linking,

    /// <summary>Linked; the connection is being opened.</summary>
    Connecting,

    /// <summary>Linked and connected: messages can be sent.</summary>
    Connected,

    /// <summary>Linked, but the connection dropped; the gateway retries by itself.</summary>
    Reconnecting,

    /// <summary>Unlinked from the phone (or the login became invalid). Must be linked again.</summary>
    LoggedOut,

    /// <summary>WhatsApp refused the number (restricted or banned). Nothing is sent.</summary>
    Restricted,

    /// <summary>The same login was opened somewhere else, so this connection stopped.</summary>
    Disconnected,
}

/// <summary>
/// The WhatsApp number one user has linked. Each user links their own number, so there is at most
/// one account per user. The saved WhatsApp login itself is kept by the gateway, encrypted, in its
/// own table; this entity only records the state the app shows.
/// </summary>
public sealed class WhatsAppAccount
{
    public const int ReasonMaxLength = 50;
    public const int ProfileNameMaxLength = 100;

    /// <summary>
    /// The gateway gives up linking after 3 minutes and reports it. If that report never comes (for example the
    /// gateway restarted mid-link), linking older than this is treated as timed out.
    /// </summary>
    public static readonly TimeSpan LinkingWindow = TimeSpan.FromMinutes(4);

    private WhatsAppAccount()
    {
    }

    public WhatsAppAccount(Guid userId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Status = WhatsAppConnectionStatus.NotLinked;
        StatusChangedAt = now;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public WhatsAppConnectionStatus Status { get; private set; }

    /// <summary>Short code for the last change, e.g. "link_timeout" or "logged_out".</summary>
    public string? StatusReason { get; private set; }

    public DateTimeOffset StatusChangedAt { get; private set; }

    /// <summary>The linked number in E.164 form, known once connected.</summary>
    public string? PhoneNumber { get; private set; }

    /// <summary>The WhatsApp profile name of the linked number.</summary>
    public string? ProfileName { get; private set; }

    /// <summary>When the current number was linked. Later used for the warm-up period of new numbers.</summary>
    public DateTimeOffset? LinkedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool CanSend => Status == WhatsAppConnectionStatus.Connected;

    public void StartLinking(DateTimeOffset now) => SetStatus(WhatsAppConnectionStatus.Linking, null, now);

    /// <summary>Ends a linking that has been waiting longer than <see cref="LinkingWindow"/>. Returns true when it did.</summary>
    public bool ExpireStaleLinking(DateTimeOffset now)
    {
        if (Status != WhatsAppConnectionStatus.Linking || now - StatusChangedAt <= LinkingWindow)
        {
            return false;
        }

        SetStatus(WhatsAppConnectionStatus.NotLinked, "link_timeout", now);
        return true;
    }

    public void MarkUnlinked(DateTimeOffset now)
    {
        SetStatus(WhatsAppConnectionStatus.NotLinked, "unlinked", now);
        PhoneNumber = null;
        ProfileName = null;
        LinkedAt = null;
    }

    /// <summary>Applies a status reported by the gateway.</summary>
    public void ApplyGatewayStatus(
        WhatsAppConnectionStatus status, string? reason, string? phoneNumber, string? profileName, DateTimeOffset now)
    {
        if (status == WhatsAppConnectionStatus.Connected)
        {
            if (reason == "linked" || LinkedAt is null || (phoneNumber is not null && phoneNumber != PhoneNumber))
            {
                LinkedAt = now;
            }

            PhoneNumber = phoneNumber ?? PhoneNumber;
            ProfileName = profileName is null ? ProfileName : Truncate(profileName, ProfileNameMaxLength);
        }
        else if (status is WhatsAppConnectionStatus.NotLinked or WhatsAppConnectionStatus.LoggedOut)
        {
            PhoneNumber = null;
            ProfileName = null;
            LinkedAt = null;
        }

        SetStatus(status, reason, now);
    }

    private void SetStatus(WhatsAppConnectionStatus status, string? reason, DateTimeOffset now)
    {
        Status = status;
        StatusReason = reason is null ? null : Truncate(reason, ReasonMaxLength);
        StatusChangedAt = now;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
