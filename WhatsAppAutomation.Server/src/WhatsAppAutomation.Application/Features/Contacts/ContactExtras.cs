using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Modules;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts;

/// <summary>Builds contact DTOs with their occasion dates and custom field values, in two queries for any number of contacts.</summary>
internal static class ContactReads
{
    public static async Task<List<ContactDto>> ToDtosAsync(IAppDbContext db, IReadOnlyList<Contact> contacts, CancellationToken cancellationToken)
    {
        if (contacts.Count == 0)
        {
            return [];
        }

        var ids = contacts.Select(c => c.Id).ToList();
        var occasions = (await db.ContactOccasions.AsNoTracking()
                .Where(o => ids.Contains(o.ContactId))
                .Join(db.OccasionModules, o => o.ModuleId, m => m.Id, (o, m) => new { o, m.SortOrder })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.o.ContactId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ContactOccasionDto>)g.OrderBy(x => x.SortOrder)
                    .Select(x => new ContactOccasionDto(x.o.ModuleId, x.o.Day, x.o.Month, x.o.Year, x.o.ForName)).ToList());
        var fields = (await db.ContactFieldValues.AsNoTracking().Where(v => ids.Contains(v.ContactId)).ToListAsync(cancellationToken))
            .GroupBy(v => v.ContactId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<Guid, string>)g.ToDictionary(v => v.FieldId, v => v.Value));
        return [.. contacts.Select(c => ContactDto.From(
            c,
            occasions.GetValueOrDefault(c.Id) ?? [],
            fields.GetValueOrDefault(c.Id) ?? new Dictionary<Guid, string>()))];
    }

    public static async Task<ContactDto> ToDtoAsync(IAppDbContext db, Contact contact, CancellationToken cancellationToken) =>
        (await ToDtosAsync(db, [contact], cancellationToken))[0];
}

/// <summary>
/// Writes a contact's occasion dates. Every occasion is stored the same way (one row per contact and occasion);
/// a Birthday date is also kept on the contact itself, so it is not lost if the Birthday occasion is deleted and
/// comes back when one is made again.
/// </summary>
internal static class ContactOccasionWrites
{
    /// <summary>Checks one date for an occasion; null when it is fine.</summary>
    public static Error? Check(OccasionModule module, int day, int month, int? year, string? forName)
    {
        if (!ModuleDates.IsValid(day, month, year))
        {
            return Error.Validation($"{module.Name}: this date does not exist.");
        }

        if (module.Kind == ModuleKind.Once && year is null)
        {
            return Error.Validation($"{module.Name}: a one-time date needs the year.");
        }

        return forName is { Length: > ContactOccasion.ForNameMaxLength }
            ? Error.Validation($"{module.Name}: the second name is too long.")
            : null;
    }

    /// <summary>Sets one date (adds or changes the row). The module and contact must be the user's own.</summary>
    public static async Task SetAsync(
        IAppDbContext db, OccasionModule module, Contact contact, int day, int month, int? year, string? forName, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entry = await db.ContactOccasions.SingleOrDefaultAsync(o => o.ModuleId == module.Id && o.ContactId == contact.Id, cancellationToken);
        if (entry is null)
        {
            entry = new ContactOccasion(module.Id, contact.Id);
            db.ContactOccasions.Add(entry);
        }

        entry.Set(day, month, year, module.Kind == ModuleKind.Birthday || module.ForNameLabel is null ? null : forName);
        if (module.Kind == ModuleKind.Birthday)
        {
            contact.SetBirthday(new Birthday(day, month, year), now);
        }
    }

    public static async Task RemoveAsync(IAppDbContext db, OccasionModule module, Contact contact, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.ContactOccasions.Where(o => o.ModuleId == module.Id && o.ContactId == contact.Id).ExecuteDeleteAsync(cancellationToken);
        if (module.Kind == ModuleKind.Birthday)
        {
            contact.SetBirthday(null, now);
        }
    }

    /// <summary>Replaces all of a contact's dates with the given ones (the contact form). Unknown occasions are refused.</summary>
    public static async Task<Error?> ReplaceAsync(
        IAppDbContext db, Guid userId, Contact contact, IReadOnlyList<ContactOccasionDto> wanted, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (wanted.Select(w => w.ModuleId).Distinct().Count() != wanted.Count)
        {
            return Error.Validation("Each occasion can be added once.");
        }

        var modules = await db.OccasionModules.Where(m => m.UserId == userId).ToListAsync(cancellationToken);
        foreach (var w in wanted)
        {
            if (modules.SingleOrDefault(m => m.Id == w.ModuleId) is not { } module)
            {
                return ModuleErrors.NotFound;
            }

            if (Check(module, w.Day, w.Month, w.Year, w.ForName) is { } invalid)
            {
                return invalid;
            }
        }

        var existing = await db.ContactOccasions.Where(o => o.ContactId == contact.Id).ToListAsync(cancellationToken);
        foreach (var gone in existing.Where(e => wanted.All(w => w.ModuleId != e.ModuleId)))
        {
            db.ContactOccasions.Remove(gone);
            if (modules.Single(m => m.Id == gone.ModuleId).Kind == ModuleKind.Birthday)
            {
                contact.SetBirthday(null, now);
            }
        }

        foreach (var w in wanted)
        {
            var module = modules.Single(m => m.Id == w.ModuleId);
            var entry = existing.SingleOrDefault(e => e.ModuleId == w.ModuleId);
            if (entry is null)
            {
                entry = new ContactOccasion(module.Id, contact.Id);
                db.ContactOccasions.Add(entry);
            }

            entry.Set(w.Day, w.Month, w.Year, module.Kind == ModuleKind.Birthday || module.ForNameLabel is null ? null : w.ForName);
            if (module.Kind == ModuleKind.Birthday)
            {
                contact.SetBirthday(new Birthday(w.Day, w.Month, w.Year), now);
            }
        }

        return null;
    }

    /// <summary>
    /// A new Birthday occasion starts with the birthdays already kept on the user's contacts (from before, or from
    /// a Birthday occasion deleted earlier), so nothing typed before is lost.
    /// </summary>
    public static async Task SeedBirthdaysAsync(IAppDbContext db, OccasionModule module, CancellationToken cancellationToken)
    {
        var dated = await db.Contacts.AsNoTracking()
            .Where(c => c.UserId == module.UserId && c.BirthDay != null && c.BirthMonth != null)
            .Select(c => new { c.Id, Day = c.BirthDay!.Value, Month = c.BirthMonth!.Value, c.BirthYear })
            .ToListAsync(cancellationToken);
        foreach (var c in dated.Where(c => ModuleDates.IsValid(c.Day, c.Month, c.BirthYear)))
        {
            var entry = new ContactOccasion(module.Id, c.Id);
            entry.Set(c.Day, c.Month, c.BirthYear, null);
            db.ContactOccasions.Add(entry);
        }
    }
}

/// <summary>Writes a contact's custom field values, checked against each field's type.</summary>
internal static class ContactFieldWrites
{
    public static async Task<Error?> ApplyAsync(
        IAppDbContext db, Guid userId, Guid contactId, IReadOnlyDictionary<Guid, string?> values, CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ids = values.Keys.ToList();
        var fields = await db.ContactFields.Where(f => f.UserId == userId && ids.Contains(f.Id)).ToListAsync(cancellationToken);
        if (fields.Count != ids.Count)
        {
            return ContactFields.ContactFieldErrors.NotFound;
        }

        var existing = await db.ContactFieldValues.Where(v => v.ContactId == contactId && ids.Contains(v.FieldId)).ToListAsync(cancellationToken);
        foreach (var field in fields)
        {
            var typed = values[field.Id];
            var current = existing.SingleOrDefault(v => v.FieldId == field.Id);
            if (string.IsNullOrWhiteSpace(typed))
            {
                if (current is not null)
                {
                    db.ContactFieldValues.Remove(current);
                }

                continue;
            }

            if (field.Normalize(typed) is not { } stored)
            {
                return Error.Validation(ContactFieldRules.ProblemText(field));
            }

            if (current is null)
            {
                db.ContactFieldValues.Add(new ContactFieldValue(field.Id, contactId, stored));
            }
            else
            {
                current.Set(stored);
            }
        }

        return null;
    }
}

internal static class ContactFieldRules
{
    public static string ProblemText(ContactField field) => field.Type switch
    {
        ContactFieldType.Number => $"{field.Name}: enter a number.",
        ContactFieldType.Date => $"{field.Name}: enter a real date (DD-MM-YYYY).",
        ContactFieldType.Choice => $"{field.Name}: choose one of {string.Join(", ", field.Options)}.",
        _ => $"{field.Name}: at most {ContactField.ValueMaxLength} characters.",
    };
}
