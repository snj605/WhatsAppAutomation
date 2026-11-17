namespace WhatsAppAutomation.Application.Features.Modules;

/// <summary>An occasion module as the menu and the module screen show it.</summary>
/// <param name="Kind">"birthday", "yearly" or "once".</param>
/// <param name="ForNameLabel">When set, each person's entry has a second name with this label, used as {{for_name}}.</param>
/// <param name="Time">"HH:mm" in the sending time zone; null when not set.</param>
/// <param name="NextRunAt">When the next automatic wishes start; null when switched off or not set up.</param>
/// <param name="TodayCount">People with this occasion today.</param>
public sealed record ModuleDto(
    Guid Id,
    string Name,
    string Kind,
    string? ForNameLabel,
    bool AutoSend,
    string? Time,
    Guid? TemplateId,
    string? TemplateName,
    DateTimeOffset? NextRunAt,
    int TodayCount,
    string TimeZone);

/// <summary>One person with this occasion today or in the coming days.</summary>
/// <param name="Date">The coming date as yyyy-MM-dd.</param>
/// <param name="DaysUntil">0 = today, 1 = tomorrow, …</param>
/// <param name="Years">Birthday: the new age. Yearly: years since the year entered. Unknown: null.</param>
/// <param name="Status">Today only: "not_sent", "pending", "sent", "delivered", "read", "failed" or "skipped".</param>
public sealed record ModuleDayPersonDto(
    Guid ContactId,
    string Name,
    string PhoneNumber,
    string Date,
    int DaysUntil,
    int? Years,
    string? ForName,
    bool OptedOut,
    string? Status,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt);

/// <summary>One person's entry in a Yearly or Once module.</summary>
public sealed record ModuleEntryDto(Guid ContactId, string Name, string PhoneNumber, int Day, int Month, int? Year, string? ForName);

public sealed record ModuleEntriesPage(IReadOnlyList<ModuleEntryDto> Items, int Total);
