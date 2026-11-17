using System.ComponentModel.DataAnnotations;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

/// <summary>
/// The fixed pacing rules, bound from the "Broadcasting" configuration section. Daily limit, chunk size, rest and
/// sending hours are each user's own (SendingSettings). The defaults are the blueprint's
/// proposed starting values. WhatsApp publishes no safe limits, so none of these is known to be safe;
/// they are deliberately slow.
/// </summary>
public sealed class BroadcastOptions
{
    public const string SectionName = "Broadcasting";

    /// <summary>Turns the background sender on or off (tests switch it off and run it step by step).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>For the automated tests only: no gaps, no rests and no sending hours, so each run sends at once.</summary>
    public bool NoWaits { get; set; }

    /// <summary>Time zone of the sending hours and of "today" for the daily limit (IANA id).</summary>
    [Required]
    public string TimeZone { get; set; } = "Asia/Kolkata";

    /// <summary>A contact gets at most one broadcast message in this many days. 0 turns the rule off.</summary>
    [Range(0, 365)]
    public int ContactGapDays { get; set; } = 0;

    /// <summary>Pause every broadcast of the number after this many failed sends in a row.</summary>
    [Range(1, 100)]
    public int MaxFailuresInRow { get; set; } = 3;

    /// <summary>
    /// Delivery health: when fewer than this share of recent messages reached the phone (two ticks), the user is
    /// told (the pace and the limit stay what the user chose). Messages that stay at one tick can mean recipients blocked the number. 0 turns it off.
    /// </summary>
    [Range(0.0, 1.0)]
    public double MinDeliveredRatio { get; set; } = 0.7;

    /// <summary>How many recent messages the delivery health looks at. Below this many it does not judge.</summary>
    [Range(5, 1000)]
    public int HealthSampleSize { get; set; } = 30;

    /// <summary>Only messages at least this old count, so phones that were briefly offline do not count as undelivered.</summary>
    [Range(0, 10080)]
    public int HealthMinAgeMinutes { get; set; } = 120;
}
