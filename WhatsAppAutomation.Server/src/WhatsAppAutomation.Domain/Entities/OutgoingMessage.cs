namespace WhatsAppAutomation.Domain.Entities;

/// <summary>The ticks of a sent message. It only moves forward: Queued, Sent, Delivered, Read.</summary>
public enum OutgoingMessageStatus
{
    /// <summary>Saved; not yet accepted by WhatsApp.</summary>
    Queued,

    /// <summary>One tick: WhatsApp's server has it.</summary>
    Sent,

    /// <summary>Two ticks: it reached the recipient's phone.</summary>
    Delivered,

    /// <summary>Blue ticks: the recipient opened it (only if they allow read receipts).</summary>
    Read,

    /// <summary>Not sent. ErrorCode says why.</summary>
    Failed,
}

/// <summary>One WhatsApp message sent from a user's linked number to one phone number.</summary>
public sealed class OutgoingMessage
{
    public const int TextMaxLength = 4096;
    public const int WhatsAppIdMaxLength = 100;
    public const int ErrorCodeMaxLength = 50;

    private OutgoingMessage()
    {
        ToPhoneNumber = string.Empty;
        Text = string.Empty;
    }

    public OutgoingMessage(Guid accountId, Guid userId, string toPhoneNumber, string text, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toPhoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > TextMaxLength)
        {
            throw new ArgumentException($"Text is longer than {TextMaxLength} characters.", nameof(text));
        }

        Id = Guid.NewGuid();
        AccountId = accountId;
        UserId = userId;
        ToPhoneNumber = toPhoneNumber;
        Text = text;
        Status = OutgoingMessageStatus.Queued;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid UserId { get; private set; }

    public string ToPhoneNumber { get; private set; }

    public string Text { get; private set; }

    public OutgoingMessageStatus Status { get; private set; }

    /// <summary>WhatsApp's id for the message, once sent.</summary>
    public string? WhatsAppMessageId { get; private set; }

    public string? ErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkSent(string whatsAppMessageId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(whatsAppMessageId);
        WhatsAppMessageId = whatsAppMessageId;
        Advance(OutgoingMessageStatus.Sent, now);
    }

    public void MarkFailed(string errorCode)
    {
        if (Status is OutgoingMessageStatus.Delivered or OutgoingMessageStatus.Read)
        {
            return;
        }

        Status = OutgoingMessageStatus.Failed;
        ErrorCode = errorCode.Length <= ErrorCodeMaxLength ? errorCode : errorCode[..ErrorCodeMaxLength];
    }

    /// <summary>
    /// Applies a tick reported by WhatsApp. Reports can arrive out of order (a "read" before the
    /// "delivered"), so a status never goes backwards. Returns false when nothing changed.
    /// </summary>
    public bool Advance(OutgoingMessageStatus status, DateTimeOffset at)
    {
        if (status == OutgoingMessageStatus.Failed)
        {
            var wasFailed = Status == OutgoingMessageStatus.Failed;
            MarkFailed("whatsapp_error");
            return !wasFailed && Status == OutgoingMessageStatus.Failed;
        }

        if (Status == OutgoingMessageStatus.Failed || status <= Status)
        {
            return false;
        }

        SentAt ??= at;
        if (status >= OutgoingMessageStatus.Delivered)
        {
            DeliveredAt ??= at;
        }

        if (status == OutgoingMessageStatus.Read)
        {
            ReadAt ??= at;
        }

        Status = status;
        return true;
    }
}
