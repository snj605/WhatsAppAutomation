using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Modules;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Import;

/// <summary>
/// Everything the user's contacts Excel needs: their categories, their own columns (occasions and fields) and one row
/// per contact with what is saved, in exactly the form the import reads back. The user edits it, adds new rows and
/// imports it again: rows are matched by phone number, so existing contacts are updated and new ones added.
/// </summary>
/// <param name="Rows">Cell text by column key (as <see cref="ImportColumns.KeyOf"/> gives it); missing keys are empty.</param>
public sealed record ContactsWorkbook(
    IReadOnlyList<string> CategoryNames,
    IReadOnlyList<(string Header, string Help)> ExtraColumns,
    bool WithBirthday,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

public sealed record GetContactsWorkbookQuery : IQuery<ContactsWorkbook>;

internal sealed class GetContactsWorkbookQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetContactsWorkbookQuery, ContactsWorkbook>
{
    public async Task<Result<ContactsWorkbook>> Handle(GetContactsWorkbookQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var categories = await db.Categories.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken);
        var birthdayModuleId = await db.OccasionModules.Where(m => m.UserId == userId && m.Kind == ModuleKind.Birthday)
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(cancellationToken);
        var occasionColumns = await ModuleColumns.ForUserAsync(db, userId, cancellationToken);
        var fieldColumns = await FieldColumns.ForUserAsync(db, userId, occasionColumns, cancellationToken);

        // Four queries for any number of contacts: contacts with groups, occasion dates, field values.
        var contacts = await db.Contacts.AsNoTracking().Include(c => c.Groups).Where(c => c.UserId == userId)
            .OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var moduleIds = occasionColumns.Select(c => c.ModuleId).Concat(birthdayModuleId is { } b ? [b] : []).ToList();
        var dates = (await db.ContactOccasions.AsNoTracking().Where(o => moduleIds.Contains(o.ModuleId)).ToListAsync(cancellationToken))
            .ToDictionary(o => (o.ModuleId, o.ContactId));
        var fieldIds = fieldColumns.Select(f => f.Field.Id).ToList();
        var values = (await db.ContactFieldValues.AsNoTracking().Where(v => fieldIds.Contains(v.FieldId)).ToListAsync(cancellationToken))
            .ToDictionary(v => (v.FieldId, v.ContactId), v => v.Value);
        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);

        var rows = new List<IReadOnlyDictionary<string, string>>(contacts.Count);
        foreach (var c in contacts)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ImportColumns.Phone] = c.PhoneNumber,
                [ImportColumns.Language] = ContactLanguages.ToCode(c.Language),
                [ImportColumns.Groups] = string.Join(", ", c.Groups.OrderBy(g => g.Name).Select(g => g.Name)),
            };
            Put(row, ImportColumns.SavedName, c.SavedName);
            Put(row, ImportColumns.NameEn, c.NameEn);
            Put(row, ImportColumns.NameHi, c.NameHi);
            Put(row, ImportColumns.NameGu, c.NameGu);
            Put(row, ImportColumns.Salutation, c.Salutation);
            Put(row, ImportColumns.Notes, c.Notes);
            Put(row, ImportColumns.Category, c.CategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null);

            if (birthdayModuleId is { } birthday)
            {
                // The Birthday occasion is where birthdays live now; the old columns are the fallback.
                Put(row, ImportColumns.Birthday, dates.TryGetValue((birthday, c.Id), out var b)
                    ? DateText(b.Day, b.Month, b.Year)
                    : c.BirthDay is { } d && c.BirthMonth is { } m ? DateText(d, m, c.BirthYear) : null);
            }

            foreach (var column in occasionColumns)
            {
                if (dates.TryGetValue((column.ModuleId, c.Id), out var o))
                {
                    Put(row, column.DateKey, DateText(o.Day, o.Month, o.Year));
                    if (column.ForNameKey is { } forNameKey)
                    {
                        Put(row, forNameKey, o.ForName);
                    }
                }
            }

            foreach (var column in fieldColumns)
            {
                if (values.TryGetValue((column.Field.Id, c.Id), out var stored))
                {
                    Put(row, column.Key, CellText(column.Field, stored));
                }
            }

            rows.Add(row);
        }

        return new ContactsWorkbook(
            [.. categories.Select(c => c.Name)],
            [.. ModuleColumns.Headers(occasionColumns), .. fieldColumns.Select(f => (f.Field.Name, FieldColumns.Help(f.Field)))],
            birthdayModuleId is not null,
            rows);
    }

    private static void Put(Dictionary<string, string> row, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            row[key] = value;
        }
    }

    /// <summary>DD-MM or DD-MM-YYYY: what the import reads.</summary>
    internal static string DateText(int day, int month, int? year) =>
        year is { } y
            ? string.Create(CultureInfo.InvariantCulture, $"{day:00}-{month:00}-{y:0000}")
            : string.Create(CultureInfo.InvariantCulture, $"{day:00}-{month:00}");

    /// <summary>A field value as people type it: dates as DD-MM-YYYY, the rest as stored.</summary>
    private static string CellText(ContactField field, string stored) =>
        field.Type == ContactFieldType.Date &&
        DateOnly.TryParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
            : stored;
}
