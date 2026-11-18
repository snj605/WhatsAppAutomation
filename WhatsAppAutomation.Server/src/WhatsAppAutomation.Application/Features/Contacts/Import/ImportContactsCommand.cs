using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Contacts.Queries;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Domain.ValueObjects;

namespace WhatsAppAutomation.Application.Features.Contacts.Import;

public enum ImportAction
{
    Create,
    Update,
    Error,
}

public sealed record ImportRowDto(
    int Row, string? PhoneNumber, string? Name, string? Category, string Action, IReadOnlyList<string> Errors);

public sealed record ImportResultDto(
    bool Applied,
    int Created,
    int Updated,
    int Failed,
    IReadOnlyList<string> NewCategories,
    IReadOnlyList<string> NewGroups,
    IReadOnlyList<ImportRowDto> Rows);

/// <summary>
/// Imports contacts from an .xlsx or .csv file. With Apply false nothing is saved: the result is the preview.
/// Rows are matched to existing contacts by phone number. On an update, empty optional cells keep the
/// saved value, and groups marked Yes are added (leaving a group is done in the app, never from a file). Only the
/// user's own master data is accepted: a category, group, salutation, occasion or choice that is not in their lists
/// is a mistake in that row, and rows with mistakes are never saved. Nothing is created from the file.
/// </summary>
public sealed record ImportContactsCommand(Stream File, string FileName, bool Apply) : ICommand<ImportResultDto>;

internal sealed class ImportContactsCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IContactSpreadsheet spreadsheet, TimeProvider clock)
    : ICommandHandler<ImportContactsCommand, ImportResultDto>
{
    public const int MaxRows = 5000;

    public async Task<Result<ImportResultDto>> Handle(ImportContactsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var read = spreadsheet.Read(command.File, command.FileName, MaxRows);
        if (read.Problem is { } problem)
        {
            return Error.Validation(problem);
        }

        if (read.MissingColumns.Count > 0)
        {
            return Error.Validation($"The file has no column: {string.Join(", ", read.MissingColumns)}. Download the template to see the columns.");
        }

        var defaultLanguage = await Preferences.DefaultLanguageAsync(db, userId, cancellationToken);
        var thisYear = clock.GetUtcNow().Year;
        var salutations = await db.Salutations.Where(s => s.UserId == userId).ToListAsync(cancellationToken);
        var categories = await db.Categories.Where(c => c.UserId == userId).ToListAsync(cancellationToken);
        var groups = await db.ContactGroups.Where(g => g.UserId == userId).ToListAsync(cancellationToken);
        var categoryByName = categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var groupByName = groups.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);
        var existing = await db.Contacts.Include(c => c.Groups).Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.PhoneNumber, cancellationToken);
        var groupColumns = GroupColumns.For(groups);

        var moduleColumns = await Modules.ModuleColumns.ForUserAsync(db, userId, cancellationToken);
        var fieldColumns = await FieldColumns.ForUserAsync(db, userId, moduleColumns, cancellationToken);
        // With a Birthday occasion, the Birthday column fills it; without one, the birthday is only kept on the contact.
        var birthdayModuleId = await db.OccasionModules.Where(m => m.UserId == userId && m.Kind == ModuleKind.Birthday)
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(cancellationToken);
        var parsed = new List<ParsedRow>();
        var seenPhones = new Dictionary<string, int>();
        foreach (var row in read.Rows)
        {
            var p = Parse(row, defaultLanguage, thisYear);
            CheckMasterData(row, p, categoryByName, groupByName, groupColumns, salutations);
            if (!p.LanguageGiven && p.Phone is { } known && existing.TryGetValue(known, out var current))
            {
                // An empty Language cell keeps a saved contact's language, like the other optional cells.
                p.Language = current.Language;
            }

            ParseOccasions(row, moduleColumns, p, thisYear);
            ParseFields(row, fieldColumns, p);
            if (birthdayModuleId is { } birthdayModule && p.Birthday is { } birthday)
            {
                p.Occasions.Add(new ParsedOccasion(birthdayModule, birthday.Day, birthday.Month, birthday.Year, null));
            }
            if (p.Errors.Count == 0 && string.IsNullOrEmpty(p.NameFor(p.Language)) &&
                !(existing.TryGetValue(p.Phone!, out var saved) && !string.IsNullOrEmpty(saved.NameIn(p.Language))))
            {
                var language = ContactLanguages.DisplayName(p.Language);
                p.Errors.Add($"Name ({language}): required, because the contact's language is {language}.");
            }

            if (p.Phone is { } phone)
            {
                if (seenPhones.TryGetValue(phone, out var firstRow))
                {
                    p.Errors.Add($"Phone: the same number is already in row {firstRow}.");
                }
                else
                {
                    seenPhones[phone] = row.RowNumber;
                }
            }

            parsed.Add(p);
        }

        // Master data is never made from a file (kept in the result for older screens: always empty).
        List<string> newCategories = [];
        List<string> newGroups = [];

        var rows = parsed.Select(p => new ImportRowDto(
            p.RowNumber,
            p.Phone ?? p.RawPhone,
            p.DisplayName,
            p.Category,
            (p.Errors.Count > 0 ? ImportAction.Error : p.Phone is not null && existing.ContainsKey(p.Phone) ? ImportAction.Update : ImportAction.Create)
                .ToString().ToLowerInvariant(),
            p.Errors)).ToList();
        var created = rows.Count(r => r.Action == "create");
        var updated = rows.Count(r => r.Action == "update");
        var failed = rows.Count(r => r.Action == "error");

        if (!command.Apply)
        {
            return new ImportResultDto(false, created, updated, failed, newCategories, newGroups, rows);
        }

        var now = clock.GetUtcNow();

        foreach (var p in parsed.Where(p => p.Errors.Count == 0))
        {
            Guid? categoryId = p.Category is null ? null : categoryByName[p.Category].Id;
            if (existing.TryGetValue(p.Phone!, out var contact))
            {
                // An empty category cell keeps the contact's category, like the other optional cells.
                categoryId ??= contact.CategoryId;
                contact.Update(
                    new ContactDetails(
                        p.Phone!,
                        p.SavedName ?? contact.SavedName,
                        p.NameEn ?? contact.NameEn,
                        p.NameHi ?? contact.NameHi,
                        p.NameGu ?? contact.NameGu,
                        p.Language,
                        categoryId,
                        p.SalutationRef,
                        p.Birthday ?? (contact.BirthDay is { } d && contact.BirthMonth is { } m ? new Birthday(d, m, contact.BirthYear) : null),
                        p.Notes ?? contact.Notes),
                    now);
            }
            else
            {
                contact = new Contact(userId, p.Phone!, now);
                contact.Update(
                    new ContactDetails(p.Phone!, p.SavedName, p.NameEn, p.NameHi, p.NameGu, p.Language, categoryId, p.SalutationRef, p.Birthday, p.Notes),
                    now);
                db.Contacts.Add(contact);
            }

            // A number already known to be without WhatsApp is not put in groups.
            foreach (var groupName in p.Groups)
            {
                if (contact.IsNotOnWhatsApp)
                {
                    break;
                }

                var group = groupByName[groupName];
                if (contact.Groups.All(g => g.Id != group.Id))
                {
                    contact.Groups.Add(group);
                }
            }
        }

        // Occasion dates: added or changed; an empty cell keeps what is saved.
        var withDates = parsed.Where(p => p.Errors.Count == 0 && p.Occasions.Count > 0).ToList();
        if (withDates.Count > 0)
        {
            var moduleIds = moduleColumns.Select(c => c.ModuleId).Concat(birthdayModuleId is { } b ? [b] : []).ToList();
            var saved = await db.ContactOccasions.Where(o => moduleIds.Contains(o.ModuleId)).ToListAsync(cancellationToken);
            var byKey = saved.ToDictionary(o => (o.ModuleId, o.ContactId));
            foreach (var p in withDates)
            {
                var contactId = existing.TryGetValue(p.Phone!, out var c) ? c.Id
                    : db.Contacts.Local.Single(x => x.UserId == userId && x.PhoneNumber == p.Phone).Id;
                foreach (var o in p.Occasions)
                {
                    if (!byKey.TryGetValue((o.ModuleId, contactId), out var entry))
                    {
                        entry = new ContactOccasion(o.ModuleId, contactId);
                        db.ContactOccasions.Add(entry);
                        byKey[(o.ModuleId, contactId)] = entry;
                    }

                    entry.Set(o.Day, o.Month, o.Year, o.ForName ?? entry.ForName);
                }
            }
        }

        // Custom field values: added or changed; an empty cell keeps what is saved.
        var withValues = parsed.Where(p => p.Errors.Count == 0 && p.FieldValues.Count > 0).ToList();
        if (withValues.Count > 0)
        {
            var fieldIds = fieldColumns.Select(f => f.Field.Id).ToList();
            var savedValues = (await db.ContactFieldValues.Where(v => fieldIds.Contains(v.FieldId)).ToListAsync(cancellationToken))
                .ToDictionary(v => (v.FieldId, v.ContactId));
            foreach (var p in withValues)
            {
                var contactId = existing.TryGetValue(p.Phone!, out var c) ? c.Id
                    : db.Contacts.Local.Single(x => x.UserId == userId && x.PhoneNumber == p.Phone).Id;
                foreach (var (fieldId, value) in p.FieldValues)
                {
                    if (savedValues.TryGetValue((fieldId, contactId), out var saved))
                    {
                        saved.Set(value);
                    }
                    else
                    {
                        var added = new ContactFieldValue(fieldId, contactId, value);
                        db.ContactFieldValues.Add(added);
                        savedValues[(fieldId, contactId)] = added;
                    }
                }
            }
        }

        // One save: the whole file is imported, or (on any database error) nothing is.
        await db.SaveChangesAsync(cancellationToken);
        return new ImportResultDto(true, created, updated, failed, newCategories, newGroups, rows);
    }

    /// <summary>
    /// Category, groups and salutation must be in the user's own lists. The Excel file offers only those in its
    /// drop-downs; a value pasted past them, or from an older file, is caught here.
    /// </summary>
    private static void CheckMasterData(
        SpreadsheetRow row, ParsedRow p, Dictionary<string, Category> categories, Dictionary<string, ContactGroup> groups,
        IReadOnlyList<GroupColumn> groupColumns, List<Salutation> salutations)
    {
        string? Cell(string key) => row.Cells.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        if (p.Category is { } category && !categories.ContainsKey(category))
        {
            p.Errors.Add($"Category: \"{category}\" is not in your list. Pick one from the list, or add it in the app first.");
        }

        if (Cell(ImportColumns.Salutation) is { } salutationText)
        {
            p.SalutationRef = Salutations.SalutationReads.Find(salutations, salutationText);
            if (p.SalutationRef is null)
            {
                p.Errors.Add($"Salutation: \"{salutationText}\" is not in your list. Pick one from the list, or add it in the app first.");
            }
        }

        foreach (var column in groupColumns)
        {
            if (Cell(column.Key) is not { } mark)
            {
                continue; // empty: nothing changes (leaving a group is done in the app)
            }

            if (GroupColumns.IsYes(mark))
            {
                if (!p.Groups.Contains(column.Group.Name, StringComparer.OrdinalIgnoreCase))
                {
                    p.Groups.Add(column.Group.Name);
                }
            }
            else
            {
                p.Errors.Add($"{column.Header}: pick Yes, or leave it empty.");
            }
        }

        foreach (var name in p.Groups.Where(g => !groups.ContainsKey(g)).ToList())
        {
            p.Errors.Add($"Groups: \"{name}\" is not one of your groups. Add it in the app first.");
            p.Groups.Remove(name);
        }
    }

    private static ParsedRow Parse(SpreadsheetRow row, ContactLanguage defaultLanguage, int thisYear)
    {
        string? Cell(string key) => row.Cells.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var p = new ParsedRow { RowNumber = row.RowNumber, RawPhone = Cell(ImportColumns.Phone) };
        if (p.RawPhone is null)
        {
            p.Errors.Add("Phone: required.");
        }
        else if (PhoneNumber.Normalize(p.RawPhone) is { } phone)
        {
            p.Phone = phone;
        }
        else
        {
            p.Errors.Add("Phone: use the international form with the country code, for example +919876543210.");
        }

        p.SavedName = Limit(Cell(ImportColumns.SavedName), Contact.NameMaxLength, "Saved name", p.Errors);
        p.NameEn = Limit(Cell(ImportColumns.NameEn), Contact.NameMaxLength, "Name (English)", p.Errors);
        p.NameHi = Limit(Cell(ImportColumns.NameHi), Contact.NameMaxLength, "Name (Hindi)", p.Errors);
        p.NameGu = Limit(Cell(ImportColumns.NameGu), Contact.NameMaxLength, "Name (Gujarati)", p.Errors);
        p.Notes = Limit(Cell(ImportColumns.Notes), Contact.NotesMaxLength, "Notes", p.Errors);
        p.Category = Limit(Cell(ImportColumns.Category), Category.NameMaxLength, "Category", p.Errors);

        var languageText = Cell(ImportColumns.Language);
        if (languageText is null)
        {
            p.Language = defaultLanguage;
        }
        else if (ContactLanguages.Parse(languageText) is { } language)
        {
            p.Language = language;
            p.LanguageGiven = true;
        }
        else
        {
            p.Errors.Add("Language: use en, hi or gu.");
        }

        if (Cell(ImportColumns.Birthday) is { } birthdayText)
        {
            p.Birthday = ParseBirthday(birthdayText, thisYear);
            if (p.Birthday is null)
            {
                p.Errors.Add("Birthday: type the date as DD-MM, DD-MM-YY or DD-MM-YYYY, for example 06-05 or 06-05-2002.");
            }
        }

        if (Cell(ImportColumns.Groups) is { } groupsText)
        {
            foreach (var name in groupsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (name.Length > ContactGroup.NameMaxLength)
                {
                    p.Errors.Add($"Groups: \"{name[..20]}…\" is longer than {ContactGroup.NameMaxLength} characters.");
                }
                else if (!p.Groups.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    p.Groups.Add(name);
                }
            }
        }

        return p;
    }

    /// <summary>The user's occasion columns: a date per module, and its second name.</summary>
    private static void ParseOccasions(SpreadsheetRow row, List<Modules.ModuleColumn> columns, ParsedRow p, int thisYear)
    {
        string? Cell(string? key) => key is not null && row.Cells.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        foreach (var column in columns)
        {
            var forName = Limit(Cell(column.ForNameKey), ContactOccasion.ForNameMaxLength, column.ForNameHeader ?? column.Name, p.Errors);
            if (Cell(column.DateKey) is not { } text)
            {
                // Given data must be complete: a second name without its date is a mistake, not half saved.
                if (forName is not null)
                {
                    p.Errors.Add($"{column.Name}: type the date too, or leave {column.ForNameHeader} empty.");
                }

                continue;
            }

            var date = ParseBirthday(text, thisYear);
            if (date is null || (column.Kind == ModuleKind.Once && date.Year is null))
            {
                p.Errors.Add(column.Kind == ModuleKind.Once
                    ? $"{column.Name}: happens one time, so type the year too: DD-MM-YYYY or DD-MM-YY, for example 06-05-2002."
                    : $"{column.Name}: type the date as DD-MM, DD-MM-YY or DD-MM-YYYY, for example 06-05 or 06-05-2002.");
                continue;
            }

            p.Occasions.Add(new ParsedOccasion(column.ModuleId, date.Day, date.Month, date.Year, forName));
        }
    }

    private static void ParseFields(SpreadsheetRow row, List<FieldColumn> columns, ParsedRow p)
    {
        foreach (var column in columns)
        {
            if (!row.Cells.TryGetValue(column.Key, out var text) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (column.Field.Normalize(text) is { } stored)
            {
                p.FieldValues.Add((column.Field.Id, stored));
            }
            else
            {
                p.Errors.Add(ContactFieldRules.ProblemText(column.Field));
            }
        }
    }

    private sealed record ParsedOccasion(Guid ModuleId, int Day, int Month, int? Year, string? ForName);

    private static string? Limit(string? value, int max, string column, List<string> errors)
    {
        if (value is not null && value.Length > max)
        {
            errors.Add($"{column}: at most {max} characters.");
        }

        return value;
    }

    /// <summary>DD-MM, DD-MM-YY or DD-MM-YYYY (see <see cref="TypedDate"/>); "/" and "." also work as separators.</summary>
    internal static Birthday? ParseBirthday(string text, int thisYear) =>
        TypedDate.Parse(text, thisYear) is var (day, month, year) ? Birthday.Create(day, month, year) : null;

    private sealed class ParsedRow
    {
        public List<ParsedOccasion> Occasions { get; } = [];

        public List<(Guid FieldId, string Value)> FieldValues { get; } = [];

        public int RowNumber { get; init; }

        public string? RawPhone { get; init; }

        public string? Phone { get; set; }

        public string? SavedName { get; set; }

        public string? NameEn { get; set; }

        public string? NameHi { get; set; }

        public string? NameGu { get; set; }

        public string? Category { get; set; }

        public ContactLanguage Language { get; set; }

        /// <summary>False when the Language cell was empty (the default language, or a saved contact's own).</summary>
        public bool LanguageGiven { get; set; }

        /// <summary>The salutation from the user's list; null when the cell is empty (the saved one stays).</summary>
        public Salutation? SalutationRef { get; set; }

        public Birthday? Birthday { get; set; }

        public string? Notes { get; set; }

        public List<string> Groups { get; } = [];

        public List<string> Errors { get; } = [];

        public string? DisplayName => SavedName ?? NameFor(Language) ?? NameEn ?? NameGu ?? NameHi;

        public string? NameFor(ContactLanguage language) => language switch
        {
            ContactLanguage.Hi => NameHi,
            ContactLanguage.Gu => NameGu,
            _ => NameEn,
        };
    }
}
