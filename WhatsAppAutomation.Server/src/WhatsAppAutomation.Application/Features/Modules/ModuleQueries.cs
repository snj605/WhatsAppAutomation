using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Modules;

internal static class ModuleReads
{
    public static DateOnly Today(BroadcastOptions options, DateTimeOffset now) => BroadcastTargets.Today(options, now);

    /// <summary>Today at the set time if today's wishes have not started yet, else tomorrow at that time.</summary>
    public static DateTimeOffset NextRun(OccasionModule m, BroadcastOptions options, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var today = Today(options, now);
        var day = m.AutoSentOn is { } done && done >= today ? today.AddDays(1) : today;
        var local = day.ToDateTime(m.SendTime!.Value);
        var at = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return at < now ? now : at;
    }

    /// <summary>Whether the set time has passed today in the sending time zone.</summary>
    public static bool TimePassedToday(TimeOnly time, BroadcastOptions options, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone));
        return TimeOnly.FromDateTime(local.DateTime) >= time;
    }

    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>The user's modules in menu order, with today's count, in two queries plus one per module for dates.</summary>
    public static async Task<List<ModuleDto>> ListAsync(
        IAppDbContext db, Guid userId, BroadcastOptions options, DateTimeOffset now, CancellationToken cancellationToken, Guid? only = null)
    {
        var modules = await db.OccasionModules.AsNoTracking()
            .Where(m => m.UserId == userId && (only == null || m.Id == only))
            .OrderBy(m => m.SortOrder).ThenBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
        var templateIds = modules.Where(m => m.TemplateId != null).Select(m => m.TemplateId!.Value).ToList();
        var templates = await db.MessageTemplates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id) && t.UserId == userId)
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var today = Today(options, now);

        var list = new List<ModuleDto>();
        foreach (var m in modules)
        {
            var people = await ModuleDates.AllAsync(db, m, cancellationToken);
            var todayCount = people.Count(p => ModuleDates.Next(m.Kind, p, today) == today);
            var templateName = m.TemplateId is { } t ? templates.GetValueOrDefault(t) : null;
            var ready = m is { AutoSend: true, SendTime: not null } && templateName is not null;
            list.Add(new ModuleDto(
                m.Id,
                m.Name,
                ModuleDates.KindCode(m.Kind),
                m.ForNameLabel,
                m.AutoSend,
                m.SendTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
                templateName is null ? null : m.TemplateId,
                templateName,
                ready ? NextRun(m, options, now) : null,
                todayCount,
                options.TimeZone));
        }

        return list;
    }
}

/// <summary>The user's occasion modules, in menu order.</summary>
public sealed record GetModulesQuery : IQuery<IReadOnlyList<ModuleDto>>;

internal sealed class GetModulesQueryHandler(IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options, TimeProvider clock)
    : IQueryHandler<GetModulesQuery, IReadOnlyList<ModuleDto>>
{
    public async Task<Result<IReadOnlyList<ModuleDto>>> Handle(GetModulesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ModuleErrors.NotSignedIn;
        }

        return await ModuleReads.ListAsync(db, userId, options.Value, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>
/// Who has this occasion today and in the next <see cref="Days"/> days, soonest first, with today's sending status
/// and delivered time per person.
/// </summary>
public sealed record GetModuleDaysQuery(Guid ModuleId, int Days = 7) : IQuery<IReadOnlyList<ModuleDayPersonDto>>;

internal sealed class GetModuleDaysQueryValidator : AbstractValidator<GetModuleDaysQuery>
{
    public GetModuleDaysQueryValidator() => RuleFor(q => q.Days).InclusiveBetween(0, 60);
}

internal sealed class GetModuleDaysQueryHandler(IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options, TimeProvider clock)
    : IQueryHandler<GetModuleDaysQuery, IReadOnlyList<ModuleDayPersonDto>>
{
    public async Task<Result<IReadOnlyList<ModuleDayPersonDto>>> Handle(GetModuleDaysQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ModuleErrors.NotSignedIn;
        }

        var module = await db.OccasionModules.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == query.ModuleId && m.UserId == userId, cancellationToken);
        if (module is null)
        {
            return ModuleErrors.NotFound;
        }

        var today = ModuleReads.Today(options.Value, clock.GetUtcNow());
        var people = await ModuleDates.AllAsync(db, module, cancellationToken);
        var coming = people
            .Select(p => (Person: p, Next: ModuleDates.Next(module.Kind, p, today)))
            .Where(x => x.Next is { } d && d.DayNumber - today.DayNumber <= query.Days)
            .ToList();

        // Today's wishes of this module: each person's newest one, with when WhatsApp took it and reached the phone.
        var todayIds = coming.Where(x => x.Next == today).Select(x => x.Person.Contact.Id).ToList();
        var wishes = await (
            from r in db.BroadcastRecipients.AsNoTracking()
            where r.ContactId != null && todayIds.Contains(r.ContactId.Value)
            join b in db.Broadcasts on r.BroadcastId equals b.Id
            where b.UserId == userId && b.Target == BroadcastTarget.Occasion && b.TargetId == module.Id && b.TargetDate == today
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

        return coming
            .Select(x =>
            {
                var date = x.Next!.Value;
                var isToday = date == today;
                var wish = isToday ? latest.GetValueOrDefault(x.Person.Contact.Id) : null;
                return new ModuleDayPersonDto(
                    x.Person.Contact.Id,
                    BroadcastPlanner.DisplayName(x.Person.Contact),
                    x.Person.Contact.PhoneNumber,
                    date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    date.DayNumber - today.DayNumber,
                    ModuleDates.Years(module.Kind, x.Person, date),
                    x.Person.ForName,
                    x.Person.Contact.OptedOutAt is not null,
                    !isToday ? null : wish is null ? "not_sent" : BroadcastStatusText.Of(wish.Status, wish.Ticks),
                    wish?.SentAt,
                    wish?.DeliveredAt);
            })
            .OrderBy(p => p.DaysUntil).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>Everyone with a date in a Yearly or Once module, by name, a page at a time.</summary>
public sealed record GetModuleEntriesQuery(Guid ModuleId, string? Search, int Page = 1, int PageSize = 50) : IQuery<ModuleEntriesPage>;

internal sealed class GetModuleEntriesQueryValidator : AbstractValidator<GetModuleEntriesQuery>
{
    public GetModuleEntriesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

internal sealed class GetModuleEntriesQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetModuleEntriesQuery, ModuleEntriesPage>
{
    public async Task<Result<ModuleEntriesPage>> Handle(GetModuleEntriesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ModuleErrors.NotSignedIn;
        }

        if (!await db.OccasionModules.AnyAsync(m => m.Id == query.ModuleId && m.UserId == userId, cancellationToken))
        {
            return ModuleErrors.NotFound;
        }

        var search = query.Search?.Trim().ToLower();
        var rows =
            from o in db.ContactOccasions.AsNoTracking()
            where o.ModuleId == query.ModuleId
            join c in db.Contacts.AsNoTracking() on o.ContactId equals c.Id
            where c.UserId == userId
            select new { o, c };
        if (!string.IsNullOrEmpty(search))
        {
            string term = search;
            rows = rows.Where(x =>
                (x.c.SavedName ?? "").ToLower().Contains(term) || (x.c.NameEn ?? "").ToLower().Contains(term) ||
                (x.c.NameGu ?? "").ToLower().Contains(term) || (x.c.NameHi ?? "").ToLower().Contains(term) ||
                (x.o.ForName ?? "").ToLower().Contains(term) || x.c.PhoneNumber.Contains(term));
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderBy(x => x.o.Month).ThenBy(x => x.o.Day).ThenBy(x => x.c.SavedName ?? x.c.NameEn ?? x.c.PhoneNumber)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new ModuleEntriesPage(
            [.. page.Select(x => new ModuleEntryDto(
                x.c.Id, BroadcastPlanner.DisplayName(x.c), x.c.PhoneNumber, x.o.Day, x.o.Month, x.o.Year, x.o.ForName))],
            total);
    }
}
