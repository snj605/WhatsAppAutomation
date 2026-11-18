namespace WhatsAppAutomation.Domain.Entities;

/// <summary>Who a broadcast was sent to, so it shows in the right chat.</summary>
public enum BroadcastTarget
{
    /// <summary>People picked by hand (a one-off chat).</summary>
    People = 0,

    /// <summary>All of the user's contacts (a one-off chat).</summary>
    Everyone = 1,

    /// <summary>One group: shows in that group's chat.</summary>
    Group = 2,

    /// <summary>One category: shows in that category's chat.</summary>
    Category = 3,

    /// <summary>
    /// Wishes of one occasion module (Birthday, Asthi visarjan, …) for one day. TargetId is the module, TargetDate the
    /// day. Occasions always go before broadcasts.
    /// </summary>
    Occasion = 4,
}

public enum BroadcastStatus
{
    /// <summary>Messages are being released one at a time by the paced sender.</summary>
    Sending,

    /// <summary>Stopped by the user or by a safety rule; PauseReason says which. Resumes only when a person presses Resume.</summary>
    Paused,

    /// <summary>Every recipient was sent, skipped or failed.</summary>
    Completed,

    /// <summary>Stopped for good by the user; recipients not yet sent are skipped.</summary>
    Cancelled,
}

/// <summary>
/// One message to many contacts. WhatsApp receives one separate one-to-one message per person, sent at a
/// human pace, each with that person's real name in the language it is sent in.
/// </summary>
public sealed class Broadcast
{
    public const int NameMaxLength = 100;
    public const int TextMaxLength = OutgoingMessage.TextMaxLength;
    public const int ReasonMaxLength = 200;

    private Broadcast()
    {
    }

    public Broadcast(Guid userId, string name, string? textEn, string? textHi, string? textGu, DateTimeOffset now, string? text = null)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Name = name.Trim();
        TextEn = Clean(textEn);
        TextHi = Clean(textHi);
        TextGu = Clean(textGu);
        Text = Clean(text);
        Status = BroadcastStatus.Sending;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// One message for everyone (the usual case). The person's details are filled in per recipient; the text is
    /// written in whatever language the user chose. Null for older broadcasts that used one text per language.
    /// </summary>
    public string? Text { get; private set; }

    /// <summary>Finished broadcasts leave the screens this long after they finish (the data is kept).</summary>
    public static readonly TimeSpan VisibleAfterFinish = TimeSpan.FromDays(5);

    public BroadcastTarget Target { get; private set; }

    /// <summary>The group or category for Group and Category targets.</summary>
    public Guid? TargetId { get; private set; }

    /// <summary>The day, for Occasion targets (in the sending time zone).</summary>
    public DateOnly? TargetDate { get; private set; }

    public void SetTarget(BroadcastTarget target, Guid? targetId, DateOnly? targetDate)
    {
        Target = target;
        TargetId = target is BroadcastTarget.Group or BroadcastTarget.Category or BroadcastTarget.Occasion ? targetId : null;
        TargetDate = target == BroadcastTarget.Occasion ? targetDate : null;
    }

    /// <summary>A file sent with the message (the message is its caption). Null when there is none.</summary>
    public Guid? AttachmentId { get; private set; }

    /// <summary>The file goes as a document (a file to download), for a video above WhatsApp's video size.</summary>
    public bool SendAsDocument { get; private set; }

    public void Attach(Guid fileId, bool asDocument = false)
    {
        AttachmentId = fileId;
        SendAsDocument = asDocument;
    }

    public string? TextEn { get; private set; }

    public string? TextHi { get; private set; }

    public string? TextGu { get; private set; }

    public BroadcastStatus Status { get; private set; }

    /// <summary>
    /// Place in the user's queue: lower goes first. Occasions always go before broadcasts whatever their place;
    /// the user moves broadcasts up and down.
    /// </summary>
    public int QueuePosition { get; private set; }

    public void PlaceInQueue(int position) => QueuePosition = position;

    public string? PauseReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public List<BroadcastRecipient> Recipients { get; private set; } = [];

    public string? TextIn(ContactLanguage language) => language switch
    {
        ContactLanguage.En => TextEn,
        ContactLanguage.Hi => TextHi,
        ContactLanguage.Gu => TextGu,
        _ => null,
    };

    public bool Pause(string reason)
    {
        if (Status != BroadcastStatus.Sending)
        {
            return false;
        }

        Status = BroadcastStatus.Paused;
        PauseReason = reason.Length <= ReasonMaxLength ? reason : reason[..ReasonMaxLength];
        return true;
    }

    public bool Resume()
    {
        if (Status != BroadcastStatus.Paused)
        {
            return false;
        }

        Status = BroadcastStatus.Sending;
        PauseReason = null;
        return true;
    }

    public bool Cancel(DateTimeOffset now)
    {
        if (Status is BroadcastStatus.Completed or BroadcastStatus.Cancelled)
        {
            return false;
        }

        Status = BroadcastStatus.Cancelled;
        PauseReason = null;
        FinishedAt = now;
        return true;
    }

    /// <summary>Sends again to recipients put back in the queue (see BroadcastRecipient.Retry).</summary>
    public void Reopen()
    {
        if (Status == BroadcastStatus.Completed)
        {
            Status = BroadcastStatus.Sending;
            FinishedAt = null;
        }
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status == BroadcastStatus.Sending)
        {
            Status = BroadcastStatus.Completed;
            FinishedAt = now;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum RecipientStatus
{
    /// <summary>Waiting for its turn.</summary>
    Pending,

    /// <summary>Handed to WhatsApp; the ticks are on the outgoing message.</summary>
    Sent,

    /// <summary>Not sent on purpose, for example a missing name. Reason says why.</summary>
    Skipped,

    /// <summary>WhatsApp did not accept it. Reason has the error code.</summary>
    Failed,
}

/// <summary>One person in a broadcast, with the exact text they get, prepared when the broadcast was created.</summary>
public sealed class BroadcastRecipient
{
    public const int NameMaxLength = 100;
    public const int ReasonMaxLength = 200;

    private BroadcastRecipient()
    {
    }

    public BroadcastRecipient(Guid broadcastId, Guid? contactId, string phoneNumber, string name, ContactLanguage? language, string? text, int order)
    {
        Id = Guid.NewGuid();
        BroadcastId = broadcastId;
        ContactId = contactId;
        PhoneNumber = phoneNumber;
        Name = name.Length <= NameMaxLength ? name : name[..NameMaxLength];
        Language = language;
        Text = text;
        Order = order;
        Status = RecipientStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid BroadcastId { get; private set; }

    /// <summary>Null once the contact is deleted; the row stays as history.</summary>
    public Guid? ContactId { get; private set; }

    public string PhoneNumber { get; private set; } = string.Empty;

    /// <summary>How the user knows them (saved name), for lists.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The language the text is in; null when skipped before a language was chosen.</summary>
    public ContactLanguage? Language { get; private set; }

    public string? Text { get; private set; }

    /// <summary>Send order within the broadcast.</summary>
    public int Order { get; private set; }

    public RecipientStatus Status { get; private set; }

    public string? Reason { get; private set; }

    public Guid? OutgoingMessageId { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public void AttachMessage(Guid outgoingMessageId) => OutgoingMessageId = outgoingMessageId;

    /// <summary>The send did not happen (WhatsApp not reachable); the recipient waits for another try.</summary>
    public void DetachMessage() => OutgoingMessageId = null;

    public void Skip(string reason, DateTimeOffset now) => Finish(RecipientStatus.Skipped, reason, now);

    public void Fail(string reason, DateTimeOffset now) => Finish(RecipientStatus.Failed, reason, now);

    /// <summary>A failed message goes back in the queue because the user asked to send it again.</summary>
    public bool Retry()
    {
        if (Status != RecipientStatus.Failed)
        {
            return false;
        }

        Status = RecipientStatus.Pending;
        Reason = null;
        OutgoingMessageId = null;
        ProcessedAt = null;
        Attempts++;
        return true;
    }

    /// <summary>How many times the user asked to send this one again.</summary>
    public int Attempts { get; private set; }

    public void MarkSent(Guid outgoingMessageId, DateTimeOffset now)
    {
        OutgoingMessageId = outgoingMessageId;
        Finish(RecipientStatus.Sent, null, now);
    }

    private void Finish(RecipientStatus status, string? reason, DateTimeOffset now)
    {
        Status = status;
        Reason = reason is null || reason.Length <= ReasonMaxLength ? reason : reason[..ReasonMaxLength];
        ProcessedAt = now;
    }
}
