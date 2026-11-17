using WhatsAppAutomation.Application.Features.Contacts.Queries;

namespace WhatsAppAutomation.Application.Tests.Features.Broadcasts;

public sealed class BirthdayDatesTests
{
    [Theory]
    [InlineData(13, 11, "2026-11-13")]   // today
    [InlineData(14, 11, "2026-11-14")]   // tomorrow
    [InlineData(12, 11, "2027-11-12")]   // yesterday: next year
    [InlineData(1, 1, "2027-01-01")]
    [InlineData(29, 2, "2027-02-28")]    // no 29 February in 2027
    public void Next_birthday_is_today_or_later(int day, int month, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), BirthdayDates.Next(day, month, new DateOnly(2026, 11, 13)));

    [Fact]
    public void A_leap_day_birthday_stays_on_29_February_in_a_leap_year() =>
        Assert.Equal(new DateOnly(2028, 2, 29), BirthdayDates.Next(29, 2, new DateOnly(2028, 1, 10)));
}
