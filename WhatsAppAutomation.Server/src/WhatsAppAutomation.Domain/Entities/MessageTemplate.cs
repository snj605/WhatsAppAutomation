namespace WhatsAppAutomation.Domain.Entities;

/// <summary>
/// A message a user saved to send again quickly, for example "Diwali wishes". Each user has their own; nobody else
/// sees them. The text may use the same placeholders as a broadcast ({{name}}, {{name_gu}}, …).
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

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, string text, DateTimeOffset now)
    {
        Name = name.Trim();
        Text = text.Trim();
        UpdatedAt = now;
    }
}
