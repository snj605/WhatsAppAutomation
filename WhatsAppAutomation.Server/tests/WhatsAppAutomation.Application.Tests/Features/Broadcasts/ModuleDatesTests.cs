using WhatsAppAutomation.Application.Features.Modules;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Tests.Features.Broadcasts;

public sealed class ModuleDatesTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static PersonDate Person(int day, int month, int? year = null) =>
        new(null!, day, month, year, null);

    [Theory]
    [InlineData(13, 11, "2026-11-13")]   // today
    [InlineData(14, 11, "2026-11-14")]   // tomorrow
    [InlineData(12, 11, "2027-11-12")]   // yesterday: next year
    [InlineData(1, 1, "2027-01-01")]
    [InlineData(29, 2, "2027-02-28")]    // no 29 February in 2027
    public void A_yearly_date_is_next_today_or_later(int day, int month, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ModuleDates.Next(ModuleKind.Yearly, Person(day, month), Today));

    [Fact]
    public void A_leap_day_stays_on_29_February_in_a_leap_year() =>
        Assert.Equal(new DateOnly(2028, 2, 29), ModuleDates.Next(ModuleKind.Birthday, Person(29, 2), new DateOnly(2028, 1, 10)));

    [Fact]
    public void A_once_date_counts_only_on_that_day()
    {
        Assert.Equal(new DateOnly(2026, 11, 20), ModuleDates.Next(ModuleKind.Once, Person(20, 11, 2026), Today));
        Assert.Null(ModuleDates.Next(ModuleKind.Once, Person(12, 11, 2026), Today));
        Assert.Null(ModuleDates.Next(ModuleKind.Once, Person(20, 11), Today));
    }

    [Fact]
    public void Years_are_the_new_age_or_years_since()
    {
        Assert.Equal(36, ModuleDates.Years(ModuleKind.Birthday, Person(13, 11, 1990), Today));
        Assert.Null(ModuleDates.Years(ModuleKind.Once, Person(13, 11, 2026), Today));
    }

    [Theory]
    [InlineData(29, 2, null, true)]
    [InlineData(29, 2, 2027, false)]
    [InlineData(31, 4, null, false)]
    [InlineData(31, 12, 2026, true)]
    public void Only_real_dates_are_valid(int day, int month, int? year, bool valid) =>
        Assert.Equal(valid, ModuleDates.IsValid(day, month, year));
}
