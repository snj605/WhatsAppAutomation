namespace WhatsAppAutomation.Domain.Entities;

/// <summary>
/// Who did what and when, for the platform admin. Append-only, and never holds message text or contacts' numbers.
/// </summary>
public sealed class AuditEntry
{
    public const int ActionMaxLength = 50;
    public const int TextMaxLength = 300;
    public const int NameMaxLength = 100;

    private AuditEntry()
    {
    }

    public AuditEntry(Guid? actorId, string actorName, Guid? tenantId, string action, string text, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ActorId = actorId;
        ActorName = Cut(actorName, NameMaxLength);
        TenantId = tenantId;
        Action = Cut(action, ActionMaxLength);
        Text = Cut(text, TextMaxLength);
        At = now;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset At { get; private set; }

    public Guid? ActorId { get; private set; }

    public string ActorName { get; private set; } = string.Empty;

    /// <summary>The tenant it concerns; null for platform-wide actions.</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>A stable code, e.g. "member.created".</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>What happened, in words, e.g. "Samir turned on Broadcasts for Priya Shah".</summary>
    public string Text { get; private set; } = string.Empty;

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Something a user should know, shown in the apps (and later pushed to the phone).</summary>
public sealed class Notification
{
    public const int KindMaxLength = 40;
    public const int TitleMaxLength = 120;
    public const int BodyMaxLength = 500;

    private Notification()
    {
    }

    public Notification(Guid userId, string kind, string title, string? body, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Kind = kind.Length <= KindMaxLength ? kind : kind[..KindMaxLength];
        Title = title.Length <= TitleMaxLength ? title : title[..TitleMaxLength];
        Body = body is null || body.Length <= BodyMaxLength ? body : body[..BodyMaxLength];
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>"whatsapp", "paused", "occasion", …</summary>
    public string Kind { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Body { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
