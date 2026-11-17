using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

/// <summary>Decides which chat a new broadcast belongs to.</summary>
internal static class BroadcastTargets
{
    /// <summary>
    /// Birthday wishes go to the day's Birthdays chat; everyone to a one-off chat; exactly one group or one category
    /// (and nothing else) to that group's or category's chat; anything else is a one-off chat of picked people.
    /// </summary>
    public static (BroadcastTarget Target, Guid? TargetId) Of(BroadcastInput input)
    {
        var groups = input.GroupIds ?? [];
        var categories = input.CategoryIds ?? [];
        var people = input.ContactIds ?? [];
        if (input.ForBirthday)
        {
            return (BroadcastTarget.Birthday, null);
        }

        if (input.AllContacts)
        {
            return (BroadcastTarget.Everyone, null);
        }

        if (groups.Count == 1 && categories.Count == 0 && people.Count == 0)
        {
            return (BroadcastTarget.Group, groups[0]);
        }

        if (categories.Count == 1 && groups.Count == 0 && people.Count == 0)
        {
            return (BroadcastTarget.Category, categories[0]);
        }

        return (BroadcastTarget.People, null);
    }

    /// <summary>Today's date in the sending time zone.</summary>
    public static DateOnly Today(BroadcastOptions options, DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone)).DateTime);
}
