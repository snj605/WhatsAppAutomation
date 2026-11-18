using WhatsAppAutomation.Application.Features.Sending;

namespace WhatsAppAutomation.Application.Tests.Features.Sending;

public sealed class HumanPaceTests
{
    /// <summary>Simulates a whole day the way the sender does: returns each wait and where the rests came.</summary>
    private static (List<double> Waits, List<int> GoLengths) Simulate(int messages, double averageSeconds, int seed)
    {
        var random = new Random(seed);
        var waits = new List<double>();
        var goes = new List<int>();
        var inGo = 0;
        for (var i = 0; i < messages; i++)
        {
            inGo++;
            waits.Add(HumanPace.NextDelay(random, averageSeconds, inGo, out var rests).TotalSeconds);
            if (rests)
            {
                goes.Add(inGo);
                inGo = 0;
            }
        }

        return (waits, goes);
    }

    [Theory]
    [InlineData(300, 12)]
    [InlineData(500, 12)]
    [InlineData(500, 8)]
    public void A_whole_day_fits_in_the_users_hours(int perDay, double hours)
    {
        var average = HumanPace.AverageSeconds(perDay, hours);
        var (waits, _) = Simulate(perDay, average, seed: 7);

        // The plan uses 85% of the hours on average; random days stay well inside the window.
        Assert.InRange(waits.Sum(), hours * 3600 * 0.6, hours * 3600);
    }

    [Fact]
    public void There_is_no_fixed_pattern_in_pauses_or_in_how_many_go_before_a_rest()
    {
        var (waits, goes) = Simulate(500, HumanPace.AverageSeconds(500, 12), seed: 11);

        Assert.True(waits.Distinct().Count() > 490, "almost every pause is different");
        Assert.True(goes.Distinct().Count() >= 8, "the number of messages before a rest keeps changing");
        Assert.All(goes, g => Assert.InRange(g, HumanPace.FewestInGo, HumanPace.MostInGo));
        Assert.All(waits, w => Assert.True(w >= HumanPace.ShortestGapSeconds));

        // Two days are never the same.
        var (otherDay, _) = Simulate(500, HumanPace.AverageSeconds(500, 12), seed: 12);
        Assert.NotEqual(waits.Take(20), otherDay.Take(20));
    }

    [Fact]
    public void Short_hours_never_go_faster_than_the_safe_average()
    {
        // 500 messages in 1 hour cannot be sent safely: the pace stays at the safe average and fewer fit.
        Assert.Equal(HumanPace.MinAverageSeconds, HumanPace.AverageSeconds(500, 1));
        Assert.Equal(153, HumanPace.FitsPerDay(500, 1));
        Assert.Equal(500, HumanPace.FitsPerDay(500, 12));
    }
}
