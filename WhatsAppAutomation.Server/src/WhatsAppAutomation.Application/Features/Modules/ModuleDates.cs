using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Modules;

/// <summary>A person's date in a module: the day and month, and the year when known.</summary>
internal sealed record PersonDate(Contact Contact, int Day, int Month, int? Year, string? ForName);

/// <summary>Who has an occasion on which day. 29 February counts on 28 February in other years.</summary>
internal static class ModuleDates
{
    public const int MaxModules = 20;

    public static string KindCode(ModuleKind kind) => kind switch
    {
        ModuleKind.Birthday => "birthday",
        ModuleKind.Once => "once",
        _ => "yearly",
    };

    public static ModuleKind? ParseKind(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "birthday" => ModuleKind.Birthday,
        "yearly" => ModuleKind.Yearly,
        "once" => ModuleKind.Once,
        _ => null,
    };

    /// <summary>Everyone with a date in this module (people not on WhatsApp are left out: they cannot get it).</summary>
    public static async Task<List<PersonDate>> AllAsync(IAppDbContext db, OccasionModule module, CancellationToken cancellationToken)
    {
        if (module.Kind == ModuleKind.Birthday)
        {
            var contacts = await db.Contacts.AsNoTracking()
                .Where(c => c.UserId == module.UserId && c.BirthDay != null && c.BirthMonth != null &&
                    c.WhatsAppCheck != WhatsAppCheck.NotOnWhatsApp)
                .ToListAsync(cancellationToken);
            return [.. contacts.Select(c => new PersonDate(c, c.BirthDay!.Value, c.BirthMonth!.Value, c.BirthYear, null))];
        }

        var rows = await (
            from o in db.ContactOccasions.AsNoTracking()
            where o.ModuleId == module.Id
            join c in db.Contacts.AsNoTracking() on o.ContactId equals c.Id
            where c.UserId == module.UserId && c.WhatsAppCheck != WhatsAppCheck.NotOnWhatsApp
            select new { c, o.Day, o.Month, o.Year, o.ForName })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new PersonDate(r.c, r.Day, r.Month, r.Year, r.ForName))];
    }

    /// <summary>The next date of this person's occasion, today or later, or null when it is over (Once, in the past).</summary>
    public static DateOnly? Next(ModuleKind kind, PersonDate p, DateOnly today)
    {
        if (kind == ModuleKind.Once)
        {
            if (p.Year is not { } year || !TryDate(year, p.Month, p.Day, out var date))
            {
                return null;
            }

            return date >= today ? date : null;
        }

        for (var year = today.Year; year <= today.Year + 1; year++)
        {
            if (TryDate(year, p.Month, p.Day, out var date) && date >= today)
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>Birthday: the new age. Yearly: years since. Once: none.</summary>
    public static int? Years(ModuleKind kind, PersonDate p, DateOnly on) =>
        kind != ModuleKind.Once && p.Year is { } year && on.Year > year ? on.Year - year : null;

    /// <summary>A real date; 29 February becomes 28 February in other years.</summary>
    public static bool TryDate(int year, int month, int day, out DateOnly date)
    {
        date = default;
        if (month is < 1 or > 12 || day < 1)
        {
            return false;
        }

        if (month == 2 && day == 29 && !DateTime.IsLeapYear(year))
        {
            day = 28;
        }

        if (day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    /// <summary>Whether a day and month (and year) can exist: 29 February is allowed without a year.</summary>
    public static bool IsValid(int day, int month, int? year) =>
        month is >= 1 and <= 12 && day >= 1 &&
        day <= (year is { } y ? DateTime.DaysInMonth(y, month) : month == 2 ? 29 : DateTime.DaysInMonth(2001, month)) &&
        year is null or (>= 1900 and <= 2200);
}
