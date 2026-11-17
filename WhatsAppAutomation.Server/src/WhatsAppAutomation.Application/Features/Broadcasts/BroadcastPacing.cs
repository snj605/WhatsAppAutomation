using System.Collections.Concurrent;
using WhatsAppAutomation.Application.Features.Sending;

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

/// <summary>The user's sending hours and "today", in the sending time zone.</summary>
internal sealed class PacingRules(BroadcastOptions options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);

    public TimeZoneInfo Zone => _zone;

    public static string Hours(UserSending s) => s.AnyTime ? "any time" : $"{s.SendFrom:HH\\:mm}–{s.SendUntil:HH\\:mm}";

    public bool InWindow(UserSending s, DateTimeOffset now)
    {
        if (s.AnyTime)
        {
            return true;
        }

        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _zone).DateTime);
        return s.SendFrom < s.SendUntil
            ? local >= s.SendFrom && local < s.SendUntil
            : local >= s.SendFrom || local < s.SendUntil;
    }

    /// <summary>The next moment (now or later) inside the sending hours.</summary>
    public DateTimeOffset NextWindowStart(UserSending s, DateTimeOffset from)
    {
        if (InWindow(s, from))
        {
            return from;
        }

        var local = TimeZoneInfo.ConvertTime(from, _zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        for (var i = 0; i < 3; i++)
        {
            var start = At(day.AddDays(i), s.SendFrom);
            if (start > from)
            {
                return start;
            }
        }

        return from;
    }

    /// <summary>Midnight today in the sending time zone, as a UTC instant.</summary>
    public DateTimeOffset StartOfToday(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, _zone);
        return At(DateOnly.FromDateTime(local.DateTime), TimeOnly.MinValue);
    }

    public DateTimeOffset At(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
