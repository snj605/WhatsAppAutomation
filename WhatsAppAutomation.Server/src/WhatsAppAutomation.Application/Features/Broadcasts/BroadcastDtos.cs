using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

/// <summary>
/// What the composer sends and who should get it (a contact in several lists is counted once). Use
/// <see cref="Text"/>: one message for everyone, with {{name}}, {{name_en}}, {{name_hi}}, {{name_gu}} and
/// {{salutation}} filled in per person. The per-language texts are the older way and are used only when Text is empty.
/// <see cref="AllContacts"/> sends to every contact of the user (the lists are then not needed).
/// <see cref="ModuleId"/> makes it today's wishes of that occasion module: they go before broadcasts, show on the
/// module's screen, and {{for_name}} is filled from each person's entry in the module.
/// </summary>
public sealed record BroadcastInput(
    string Name,
    string? TextEn,
    string? TextHi,
    string? TextGu,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? GroupIds,
    IReadOnlyList<Guid>? ContactIds,
    string? Text = null,
    bool AllContacts = false,
    Guid? AttachmentId = null,
    Guid? ModuleId = null,
    bool SendVideoAsDocument = false)
{
    /// <summary>One contact picked by hand: a single message, which every member may send.</summary>
    public bool IsSingleMessage =>
        !AllContacts && ModuleId is null && CategoryIds is not { Count: > 0 } && GroupIds is not { Count: > 0 } &&
        ContactIds is { Count: 1 };
}

/// <summary>The file sent with a broadcast; Kept is false once its bytes were freed after sending.</summary>
public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, bool Kept);

public sealed record SkippedRecipientDto(Guid? ContactId, string Name, string PhoneNumber, string Reason);

/// <param name="Language">"en", "hi" or "gu" for per-language texts; "all" for a single message.</param>
public sealed record SampleMessageDto(string Language, string ContactName, string Text);

/// <summary>One person the message goes to, for the "Preview for" picker.</summary>
public sealed record PreviewPersonDto(Guid ContactId, string Name);

/// <summary>Exactly what one person gets: their text, or why they are left out.</summary>
public sealed record PersonMessageDto(Guid ContactId, string Name, string PhoneNumber, string? Text, string? SkipReason);

/// <param name="People">The first 100 people it goes to (or is checked for), for the "Preview for" picker.</param>
/// <param name="Person">The text of the person asked for with ?for=, when they are among the people.</param>
public sealed record BroadcastPreviewDto(
    int Total,
    int WillSend,
    IReadOnlyDictionary<string, int> ByLanguage,
    IReadOnlyList<SkippedRecipientDto> Skipped,
    IReadOnlyList<SampleMessageDto> Samples,
    int DailyLimit,
    int EstimatedDays,
    IReadOnlyList<PreviewPersonDto> People,
    PersonMessageDto? Person);

public sealed record BroadcastCountsDto(int Total, int Pending, int Sent, int Delivered, int Read, int Failed, int Skipped);

public sealed record BroadcastSummaryDto(
    Guid Id,
    string Name,
    string Status,
    string? PauseReason,
    IReadOnlyList<string> Languages,
    BroadcastCountsDto Counts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);

public sealed record RecipientDto(
    Guid Id,
    Guid? ContactId,
    string Name,
    string PhoneNumber,
    string? Language,
    string? Text,
    string Status,
    string? Reason,
    DateTimeOffset? ProcessedAt);

public sealed record BroadcastDetailsDto(
    BroadcastSummaryDto Summary,
    string? TextEn,
    string? TextHi,
    string? TextGu,
    IReadOnlyList<RecipientDto> Recipients,
    string? Text = null,
    AttachmentDto? Attachment = null);

/// <summary>Where the paced sender stands for the user's number, for the screens.</summary>
public sealed record PacingDto(
    bool Connected,
    int SentToday,
    int DailyLimit,
    bool InSendingHours,
    string SendingHours,
    DateTimeOffset? NextSendAt,
    string? Waiting);

internal static class BroadcastStatusText
{
    public static string Of(BroadcastStatus status) => status.ToString().ToLowerInvariant();

    /// <summary>"sent" also covers delivered and read: the screen shows the ticks from the outgoing message.</summary>
    public static string Of(RecipientStatus status, OutgoingMessageStatus? ticks) => status switch
    {
        RecipientStatus.Sent when ticks is OutgoingMessageStatus.Read => "read",
        RecipientStatus.Sent when ticks is OutgoingMessageStatus.Delivered => "delivered",
        RecipientStatus.Sent when ticks is OutgoingMessageStatus.Failed => "failed",
        _ => status.ToString().ToLowerInvariant(),
    };

    public static IReadOnlyList<string> Languages(Broadcast b) =>
        new[] { (ContactLanguage.En, b.TextEn), (ContactLanguage.Hi, b.TextHi), (ContactLanguage.Gu, b.TextGu) }
            .Where(t => t.Item2 is not null).Select(t => ContactLanguages.ToCode(t.Item1)).ToList();
}
