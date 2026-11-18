namespace WhatsAppAutomation.Domain.Entities;

/// <summary>
/// A message saved to send again quickly, for example "Diwali wishes". Each user has their own; a tenant admin can
/// also share one with everyone in the tenant (the tenant's library). The text may use the same placeholders as a
/// broadcast ({{name}}, {{name_gu}}, {{for_name}}, …). A file can be attached (uploaded or taken from a Drive link).
/// </summary>
public sealed class MessageTemplate
{
    public const int NameMaxLength = 60;
    public const int TextMaxLength = OutgoingMessage.TextMaxLength;

    private MessageTemplate()
    {
    }

    public MessageTemplate(Guid userId, string name, string text, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CreatedAt = now;
        Update(name, text, now);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    /// <summary>The creator's tenant (null for templates saved before tenants existed).</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>In the tenant's library: everyone in the tenant may use it; only its admins change it.</summary>
    public bool IsShared { get; private set; }

    public Guid? AttachmentId { get; private set; }

    public void Share(Guid? tenantId, bool shared)
    {
        TenantId = tenantId;
        IsShared = shared && tenantId is not null;
    }

    public void SetAttachment(Guid? fileId) => AttachmentId = fileId;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, string text, DateTimeOffset now)
    {
        Name = name.Trim();
        Text = text.Trim();
        UpdatedAt = now;
    }
}
