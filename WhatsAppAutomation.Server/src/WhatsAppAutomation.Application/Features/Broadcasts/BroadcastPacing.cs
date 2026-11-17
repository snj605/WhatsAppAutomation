using System.Collections.Concurrent;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

/// <summary>
/// What the pacing screen shows per WhatsApp number: when the next message goes and why it waits. Only a copy
/// for display, kept in memory; the real pacing state (next send, rests, failures) is the SendingPace row.
/// </summary>
public sealed class BroadcastPacing
{
    private readonly ConcurrentDictionary<Guid, AccountPacing> _accounts = new();

    /// <summary>When stored files were last freed (see FileCleanup).</summary>
    public DateTimeOffset LastFileCleanup { get; set; }

    public AccountPacing For(Guid accountId, Func<AccountPacing> create) => _accounts.GetOrAdd(accountId, _ => create());

    public AccountPacing? Find(Guid accountId) => _accounts.GetValueOrDefault(accountId);
}

public sealed class AccountPacing
{
    public DateTimeOffset NextSendAt { get; set; }

    /// <summary>Why nothing is being sent right now, in words for the screen; null while sending normally.</summary>
    public string? Waiting { get; set; }
}

/// <summary>Daily limit and sending hours, worked out from the options.</summary>
internal sealed class PacingRules(BroadcastOptions options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);

    public string SendingHours => options.SendFrom == options.SendUntil
        ? "any time"
        : $"{options.SendFrom:HH\\:mm}–{options.SendUntil:HH\\:mm} ({options.TimeZone})";

    public bool InSendingHours(DateTimeOffset now)
    {
        if (options.SendFrom == options.SendUntil)
        {
            return true;
        }

        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _zone).DateTime);
        return options.SendFrom < options.SendUntil
            ? local >= options.SendFrom && local < options.SendUntil
            : local >= options.SendFrom || local < options.SendUntil;
    }

    /// <summary>Midnight today in the configured time zone, as a UTC instant.</summary>
    public DateTimeOffset StartOfToday(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, _zone);
        var midnight = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, _zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    /// <summary>1 in the first week after linking, 2 in the second… null once warm-up is over.</summary>
    public int? WarmUpWeek(WhatsAppAccount account, DateTimeOffset now)
    {
        if (account.LinkedAt is not { } linkedAt)
        {
            return options.WarmUp.Length > 0 ? 1 : null;
        }

        var week = (int)((now - linkedAt).TotalDays / 7) + 1;
        return week <= options.WarmUp.Length ? week : null;
    }

    public int DailyCap(WhatsAppAccount account, DateTimeOffset now) =>
        WarmUpWeek(account, now) is { } week ? Math.Min(options.WarmUp[week - 1], options.DailyCap) : options.DailyCap;
}
