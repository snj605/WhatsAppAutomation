using System.ComponentModel.DataAnnotations;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

/// <summary>
/// The pacing rules, bound from the "Broadcasting" configuration section. The defaults are the blueprint's
/// proposed starting values. WhatsApp publishes no safe limits, so none of these is known to be safe;
/// they are deliberately slow.
/// </summary>
public sealed class BroadcastOptions
{
    public const string SectionName = "Broadcasting";

    /// <summary>Turns the background sender on or off (tests switch it off and run it step by step).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Time zone of the sending hours and of "today" for the daily limit (IANA id).</summary>
    [Required]
    public string TimeZone { get; set; } = "Asia/Kolkata";

    /// <summary>Start of the sending hours, local time. Equal to SendUntil means any time of day.</summary>
    public TimeOnly SendFrom { get; set; } = new(9, 0);

    public TimeOnly SendUntil { get; set; } = new(20, 0);

    [Range(0, 3600)]
    public int MinDelaySeconds { get; set; } = 45;

    [Range(0, 3600)]
    public int MaxDelaySeconds { get; set; } = 120;

    /// <summary>After this many messages in a row, rest for a while.</summary>
    [Range(1, 1000)]
    public int BatchSize { get; set; } = 10;

    [Range(0, 600)]
    public int BatchRestMinMinutes { get; set; } = 10;

    [Range(0, 600)]
    public int BatchRestMaxMinutes { get; set; } = 20;

    /// <summary>Most messages per day from one number once warm-up is over (test messages count too).</summary>
    [Range(1, 10000)]
    public int DailyCap { get; set; } = 150;

    /// <summary>
    /// Daily limits in the first weeks after a number is linked: week 1, week 2, … Not set: 20, 40, 75.
    /// (Null by default because configuration binding adds to an existing array instead of replacing it.)
    /// </summary>
    public int[]? WarmUpCaps { get; set; }

    internal int[] WarmUp => WarmUpCaps ?? [20, 40, 75];

    /// <summary>A contact gets at most one broadcast message in this many days. 0 turns the rule off.</summary>
    [Range(0, 365)]
    public int ContactGapDays { get; set; } = 0;

    /// <summary>Pause every broadcast of the number after this many failed sends in a row.</summary>
    [Range(1, 100)]
    public int MaxFailuresInRow { get; set; } = 3;

    /// <summary>
    /// Delivery health: when fewer than this share of recent messages reached the phone (two ticks), sending
    /// slows down. Messages that stay at one tick can mean recipients blocked the number. 0 turns it off.
    /// </summary>
    [Range(0.0, 1.0)]
    public double MinDeliveredRatio { get; set; } = 0.7;

    /// <summary>How many recent messages the delivery health looks at. Below this many it does not judge.</summary>
    [Range(5, 1000)]
    public int HealthSampleSize { get; set; } = 30;

    /// <summary>Only messages at least this old count, so phones that were briefly offline do not count as undelivered.</summary>
    [Range(0, 10080)]
    public int HealthMinAgeMinutes { get; set; } = 120;

    /// <summary>While delivery is poor: delays are multiplied by this and the daily limit divided by it.</summary>
    [Range(1, 10)]
    public int SlowDownFactor { get; set; } = 2;
}
