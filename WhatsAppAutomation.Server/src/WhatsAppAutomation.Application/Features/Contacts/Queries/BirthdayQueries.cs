using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Queries;

/// <summary>One contact whose birthday is today or soon.</summary>
/// <param name="DaysUntil">0 = today, 1 = tomorrow, …</param>
/// <param name="Date">The coming birthday as yyyy-MM-dd (in the sending time zone).</param>
/// <param name="TurnsAge">Their new age, when the birth year is known.</param>
public sealed record UpcomingBirthdayDto(
    Guid ContactId,
    string Name,
    string PhoneNumber,
    string Language,
    string Date,
    int DaysUntil,
    int? TurnsAge,
    bool OptedOut,
    string? Status = null,
    DateTimeOffset? SentAt = null,
    DateTimeOffset? DeliveredAt = null);

/// <summary>
/// The user's contacts with a birthday today or in the next <see cref="Days"/> days, soonest first. "Today" is
/// the date in the broadcasting time zone. Someone born on 29 February is shown on 28 February in other years.
/// </summary>
public sealed record GetUpcomingBirthdaysQuery(int Days = 7) : IQuery<IReadOnlyList<UpcomingBirthdayDto>>;

internal sealed class GetUpcomingBirthdaysQueryValidator : AbstractValidator<GetUpcomingBirthdaysQuery>
{
    public GetUpcomingBirthdaysQueryValidator() => RuleFor(q => q.Days).InclusiveBetween(0, 60);
}

internal sealed class GetUpcomingBirthdaysQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options, TimeProvider clock)
    : IQueryHandler<GetUpcomingBirthdaysQuery, IReadOnlyList<UpcomingBirthdayDto>>
{
    public async Task<Result<IReadOnlyList<UpcomingBirthdayDto>>> Handle(GetUpcomingBirthdaysQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);

        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.UserId == userId && c.BirthDay != null && c.BirthMonth != null && c.WhatsAppCheck != WhatsAppCheck.NotOnWhatsApp)
            .ToListAsync(cancellationToken);

        // Today's wishes: each person's newest one, with when WhatsApp took it and when it reached the phone.
        var todayIds = contacts.Select(c => c.Id).ToList();
        var wishes = await (
            from r in db.BroadcastRecipients.AsNoTracking()
            where r.ContactId != null && todayIds.Contains(r.ContactId.Value)
            join b in db.Broadcasts on r.BroadcastId equals b.Id
            where b.UserId == userId && b.Target == BroadcastTarget.Birthday && b.TargetDate == today
            join m in db.OutgoingMessages on r.OutgoingMessageId equals m.Id into messages
            from m in messages.DefaultIfEmpty()
            select new
            {
                ContactId = r.ContactId!.Value,
                r.Status,
                Ticks = (OutgoingMessageStatus?)m.Status,
                SentAt = (DateTimeOffset?)m.SentAt,
                DeliveredAt = (DateTimeOffset?)m.DeliveredAt,
                b.CreatedAt,
            })
            .ToListAsync(cancellationToken);
        var latest = wishes.GroupBy(w => w.ContactId).ToDictionary(g => g.Key, g => g.MaxBy(w => w.CreatedAt)!);

        var list = new List<UpcomingBirthdayDto>();
        foreach (var c in contacts)
        {
            var next = BirthdayDates.Next(c.BirthDay!.Value, c.BirthMonth!.Value, today);
            var daysUntil = next.DayNumber - today.DayNumber;
            if (daysUntil > query.Days)
            {
                continue;
            }

            list.Add(new UpcomingBirthdayDto(
                c.Id,
                BroadcastPlanner.DisplayName(c),
                c.PhoneNumber,
                ContactLanguages.ToCode(c.Language),
                next.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                daysUntil,
                c.BirthYear is { } year ? next.Year - year : null,
                c.OptedOutAt is not null,
                daysUntil > 0 ? null : latest.TryGetValue(c.Id, out var w) ? BroadcastStatusText.Of(w.Status, w.Ticks) : "not_sent",
                daysUntil > 0 ? null : latest.GetValueOrDefault(c.Id)?.SentAt,
                daysUntil > 0 ? null : latest.GetValueOrDefault(c.Id)?.DeliveredAt));
        }

        return list.OrderBy(b => b.DaysUntil).ThenBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

internal static class BirthdayDates
{
    /// <summary>The next date (today or later) of a day and month. 29 February becomes 28 February in non-leap years.</summary>
    public static DateOnly Next(int day, int month, DateOnly today)
    {
        for (var year = today.Year; ; year++)
        {
            var d = month == 2 && day == 29 && !DateTime.IsLeapYear(year) ? 28 : day;
            var date = new DateOnly(year, month, d);
            if (date >= today)
            {
                return date;
            }
        }
    }
}
