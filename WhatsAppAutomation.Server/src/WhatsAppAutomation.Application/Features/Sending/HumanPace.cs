namespace WhatsAppAutomation.Application.Features.Sending;

/// <summary>
/// The sending rhythm, chosen by the system so it never forms a pattern: the user sets only how many messages a
/// day and the hours. From those comes the average time per message; around it every pause is drawn at random
/// (skewed, so most are near the average and a few are much longer), a rest comes after a random number of
/// messages (never a fixed "N then rest"), each rest has a random length, and now and then a short extra break
/// comes in the middle, the way a person gets distracted.
/// </summary>
internal static class HumanPace
{
    /// <summary>The fastest average the system allows, whatever the hours: about 3 messages a minute.</summary>
    public const double MinAverageSeconds = 20;

    /// <summary>The slowest average, so a long window with few messages does not wait for ages between them.</summary>
    public const double MaxAverageSeconds = 300;

    /// <summary>The plan uses this share of the sending hours, keeping room for slow sends and retries.</summary>
    public const double WindowShare = 0.85;

    /// <summary>No pause is shorter than this.</summary>
    public const double ShortestGapSeconds = 8;

    /// <summary>A rest never comes before this many messages, and always by this many.</summary>
    public const int FewestInGo = 3;

    public const int MostInGo = 35;

    /// <summary>Average messages between two rests (the chance of a rest after each message is 1 in this).</summary>
    public const double AverageInGo = 14;

    /// <summary>Share of the time spent in rests; the rest goes to the pauses between messages.</summary>
    public const double RestShare = 0.35;

    /// <summary>Chance of a short extra break (one to four minutes) after any message.</summary>
    public const double BreakChance = 0.04;

    /// <summary>The average time one message takes (pause and its share of the rests), in seconds.</summary>
    public static double AverageSeconds(int dailyLimit, double windowHours) =>
        Math.Clamp(windowHours * 3600 * WindowShare / Math.Max(1, dailyLimit), MinAverageSeconds, MaxAverageSeconds);

    /// <summary>How many messages fit in a day with these hours (at most the daily limit).</summary>
    public static int FitsPerDay(int dailyLimit, double windowHours) =>
        Math.Min(dailyLimit, (int)Math.Floor(windowHours * 3600 * WindowShare / MinAverageSeconds));

    /// <summary>
    /// The wait after a message. <paramref name="sentInGo"/> is how many went since the last rest, this one
    /// included; <paramref name="rests"/> is set when this wait is a rest (the count then starts again).
    /// </summary>
    public static TimeSpan NextDelay(Random random, double averageSeconds, int sentInGo, out bool rests)
    {
        // The short extra breaks take BreakChance × 150 s per message on average: taken out of the pauses.
        var gapMean = Math.Max(ShortestGapSeconds, (averageSeconds * (1 - RestShare)) - (BreakChance * 150));
        var restMean = averageSeconds * RestShare * AverageInGo;

        // Log-normal: most pauses close to the mean, some short, a few long. Divided so the mean stays gapMean.
        const double sigma = 0.5;
        var normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        var gap = gapMean * Math.Exp(sigma * normal - (sigma * sigma / 2));
        gap = Math.Clamp(gap, ShortestGapSeconds, Math.Max(ShortestGapSeconds, gapMean * 4));

        rests = sentInGo >= MostInGo || (sentInGo >= FewestInGo && random.NextDouble() < 1 / AverageInGo);
        if (rests)
        {
            gap += restMean * (0.5 + random.NextDouble());
        }
        else if (random.NextDouble() < BreakChance)
        {
            gap += 60 + (random.NextDouble() * 180);
        }

        return TimeSpan.FromSeconds(gap);
    }
}
