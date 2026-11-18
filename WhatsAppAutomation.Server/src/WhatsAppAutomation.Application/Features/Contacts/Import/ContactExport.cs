using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Modules;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Import;

/// <summary>One column of the contacts Excel file and what may go in it.</summary>
/// <param name="Key">The import key (<see cref="ImportColumns.KeyOf"/> of the header).</param>
/// <param name="List">The name of a pick-list in <see cref="ContactsWorkbook.Lists"/>: only its values may be chosen.</param>
/// <param name="Number">Only a number may be typed.</param>
/// <param name="MaxLength">The longest text allowed, when there is a limit.</param>
public sealed record WorkbookColumn(string Key, string Header, string Help, bool Required, string? List, bool Number, int? MaxLength);

/// <summary>A pick-list (for example the user's categories). An empty one allows nothing yet.</summary>
public sealed record WorkbookList(string Name, IReadOnlyList<string> Values, string EmptyMessage);

/// <summary>
/// Everything the user's contacts Excel needs: every column with its rule (pick-lists from the user's own master
/// data, numbers, lengths) and one row per contact with what is saved, in exactly the form the import reads back.
/// The user edits it, adds new rows and imports it again: rows are matched by phone number, so existing contacts
/// are updated and new ones added. The import checks the same rules again (Excel's checks can be pasted past).
/// </summary>
/// <param name="Rows">Cell text by column key; missing keys are empty.</param>
public sealed record ContactsWorkbook(
    IReadOnlyList<WorkbookColumn> Columns,
    IReadOnlyList<WorkbookList> Lists,
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
        var groups = await db.ContactGroups.AsNoTracking().Where(g => g.UserId == userId).ToListAsync(cancellationToken);
        var salutations = await db.Salutations.AsNoTracking().Where(s => s.UserId == userId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.NameEn).ToListAsync(cancellationToken);
        var birthdayModuleId = await db.OccasionModules.Where(m => m.UserId == userId && m.Kind == ModuleKind.Birthday)
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(cancellationToken);
        var occasionColumns = await ModuleColumns.ForUserAsync(db, userId, cancellationToken);
        var fieldColumns = await FieldColumns.ForUserAsync(db, userId, occasionColumns, cancellationToken);
        var groupColumns = GroupColumns.For(groups);

        var (columns, lists) = Layout(birthdayModuleId is not null, categories.Select(c => c.Name).ToList(), salutations,
            occasionColumns, fieldColumns, groupColumns);

        // Few queries for any number of contacts: contacts with groups, occasion dates, field values.
        var contacts = await db.Contacts.AsNoTracking().Include(c => c.Groups).Where(c => c.UserId == userId)
            .OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var moduleIds = occasionColumns.Select(c => c.ModuleId).Concat(birthdayModuleId is { } birthdayId ? [birthdayId] : []).ToList();
        var dates = (await db.ContactOccasions.AsNoTracking().Where(o => moduleIds.Contains(o.ModuleId)).ToListAsync(cancellationToken))
            .ToDictionary(o => (o.ModuleId, o.ContactId));
        var fieldIds = fieldColumns.Select(f => f.Field.Id).ToList();
        var values = (await db.ContactFieldValues.AsNoTracking().Where(v => fieldIds.Contains(v.FieldId)).ToListAsync(cancellationToken))
            .ToDictionary(v => (v.FieldId, v.ContactId), v => v.Value);
        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);
        var salutationLabels = salutations.ToDictionary(s => s.Id, s => s.Label);

        var rows = new List<IReadOnlyDictionary<string, string>>(contacts.Count);
        foreach (var c in contacts)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ImportColumns.Phone] = c.PhoneNumber,
                [ImportColumns.Language] = ContactLanguages.DisplayName(c.Language),
            };
            Put(row, ImportColumns.SavedName, c.SavedName);
            Put(row, ImportColumns.NameEn, c.NameEn);
            Put(row, ImportColumns.NameHi, c.NameHi);
            Put(row, ImportColumns.NameGu, c.NameGu);
            Put(row, ImportColumns.Salutation, c.SalutationId is { } sid ? salutationLabels.GetValueOrDefault(sid) : null);
            Put(row, ImportColumns.Notes, c.Notes);
            Put(row, ImportColumns.Category, c.CategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null);
            foreach (var column in groupColumns.Where(g => c.Groups.Any(x => x.Id == g.Group.Id)))
            {
                row[column.Key] = GroupColumns.Yes;
            }

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

        return new ContactsWorkbook(columns, lists, rows);
    }

    /// <summary>
    /// The columns in order with their rules: the standard ones, then a Yes column per group, a date (and second
    /// name) per occasion, and one per contact field. Every column with master data is a pick-list.
    /// </summary>
    internal static (List<WorkbookColumn> Columns, List<WorkbookList> Lists) Layout(
        bool withBirthday, IReadOnlyList<string> categories, IReadOnlyList<Salutation> salutations,
        IReadOnlyList<ModuleColumn> occasions, IReadOnlyList<FieldColumn> fields, IReadOnlyList<GroupColumn> groups)
    {
        const string dateHelp = "Type the date as DD-MM, DD-MM-YY or DD-MM-YYYY, for example 06-05 or 06-05-2002.";
        var lists = new List<WorkbookList>
        {
            new("Languages", [.. Enum.GetValues<ContactLanguage>().Select(ContactLanguages.DisplayName)], "Pick a language."),
            new("Categories", categories, "You have no categories yet: add them in the app first."),
            new("Salutations", [.. salutations.Select(s => s.Label)], "You have no salutations yet: add them in the app first (Occasions → Salutations)."),
            new("YesList", [GroupColumns.Yes], "Pick Yes."),
        };
        var columns = new List<WorkbookColumn>();
        foreach (var (key, header, help) in ImportColumns.All)
        {
            if (key == ImportColumns.Groups || (key == ImportColumns.Birthday && !withBirthday))
            {
                continue; // groups get a column each; Birthday only with a Birthday occasion
            }

            columns.Add(key switch
            {
                ImportColumns.Phone => new(key, header, help, true, null, false, 30),
                ImportColumns.Language => new(key, header, help, false, "Languages", false, null),
                ImportColumns.Category => new(key, header, help, false, "Categories", false, null),
                ImportColumns.Salutation => new(key, header, help, false, "Salutations", false, null),
                ImportColumns.Birthday => new(key, header, dateHelp, false, null, false, 10),
                ImportColumns.Notes => new(key, header, help, false, null, false, Contact.NotesMaxLength),
                _ => new(key, header, help, false, null, false, Contact.NameMaxLength),
            });
        }

        columns.AddRange(groups.Select(g => new WorkbookColumn(
            g.Key, g.Header, $"Yes puts the person in the group {g.Group.Name}. Empty changes nothing (to take someone out of a group, use the app).",
            false, "YesList", false, null)));

        foreach (var o in occasions)
        {
            columns.Add(new(o.DateKey, o.Name, o.Kind == ModuleKind.Once
                ? $"For \"{o.Name}\": happens one time, so the year is needed. {dateHelp}"
                : $"For \"{o.Name}\". {dateHelp} Empty keeps what is saved.", false, null, false, 10));
            if (o.ForNameKey is { } forNameKey)
            {
                columns.Add(new(forNameKey, o.ForNameHeader!, $"For \"{o.Name}\": the name used as {{{{for_name}}}}. Only with a date.",
                    false, null, false, ContactOccasion.ForNameMaxLength));
            }
        }

        foreach (var f in fields)
        {
            var listName = $"Field{f.Field.Id:N}";
            if (f.Field.Type == ContactFieldType.Choice)
            {
                lists.Add(new(listName, f.Field.Options, $"Pick one of the choices of {f.Field.Name}."));
            }

            columns.Add(f.Field.Type switch
            {
                ContactFieldType.Choice => new(f.Key, f.Field.Name, $"Pick one of: {string.Join(", ", f.Field.Options)}.", false, listName, false, null),
                ContactFieldType.Number => new(f.Key, f.Field.Name, "A number.", false, null, true, null),
                ContactFieldType.Date => new(f.Key, f.Field.Name, "Type the date as DD-MM-YYYY or DD-MM-YY, for example 06-05-2002.", false, null, false, 10),
                _ => new(f.Key, f.Field.Name, "Text.", false, null, false, ContactField.ValueMaxLength),
            });
        }

        return (columns, lists);
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
