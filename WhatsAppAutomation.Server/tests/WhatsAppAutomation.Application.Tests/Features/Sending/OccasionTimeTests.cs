using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Broadcasts.Queries;
using WhatsAppAutomation.Application.Features.Sending;

namespace WhatsAppAutomation.Application.Tests.Features.Sending;

/// <summary>Occasion wishes start at their own time, even before the sending hours; broadcasts keep to the hours.</summary>
public sealed class OccasionTimeTests
{
    private static readonly PacingRules Rules = new(new BroadcastOptions { TimeZone = "Asia/Kolkata" });

    // 8:00 AM to 9:00 PM, 300 a day.
    private static readonly UserSending Hours = new(300, new TimeOnly(8, 0), new TimeOnly(21, 0));

    private static DateTimeOffset At(int hour, int minute = 0) => Rules.At(new DateOnly(2026, 11, 20), new TimeOnly(hour, minute));

    [Fact]
    public void Todays_wishes_may_go_before_the_sending_hours_but_a_broadcast_waits()
    {
        var sevenAm = At(7);
        var today = new DateOnly(2026, 11, 20);

        Assert.True(Rules.MaySend(Hours, sevenAm, occasionDay: today));
        Assert.False(Rules.MaySend(Hours, sevenAm, occasionDay: null));
        // Yesterday's wishes that are still waiting keep to the sending hours.
        Assert.False(Rules.MaySend(Hours, sevenAm, occasionDay: today.AddDays(-1)));
        Assert.True(Rules.MaySend(Hours, At(9), occasionDay: null));
    }

    [Fact]
    public void Thirty_wishes_from_7_am_are_planned_before_8_and_the_broadcast_starts_at_8()
    {
        var today = new DateOnly(2026, 11, 20);
        var busyHours = Hours with { DailyLimit = 500 };
        var plan = new SendPlan(Rules, busyHours, sentToday: 0, Rules.StartOfToday(At(7)), At(7));

        var wishes = plan.Take(30, today);
        var broadcast = plan.Take(100);

        Assert.Equal(At(7), wishes.First);
        // 500 a day over 13 hours is about 80 seconds a message: 30 wishes take about 40 minutes.
        Assert.InRange(wishes.Last, At(7, 30), At(7, 59));
        Assert.Equal(At(8), broadcast.First);
    }

    [Fact]
    public void Wishes_and_broadcasts_share_the_one_daily_limit()
    {
        var today = new DateOnly(2026, 11, 20);
        var plan = new SendPlan(Rules, Hours, sentToday: 290, Rules.StartOfToday(At(7)), At(7));

        plan.Take(10, today);
        var broadcast = plan.Take(1);

        // The limit of 300 is used up by the wishes: the broadcast goes tomorrow at 8:00.
        Assert.Equal(Rules.At(today.AddDays(1), new TimeOnly(8, 0)), broadcast.First);
    }
}
