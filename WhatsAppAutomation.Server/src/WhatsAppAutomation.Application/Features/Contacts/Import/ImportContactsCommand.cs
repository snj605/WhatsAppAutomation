using System.Globalization;
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
/// saved value, and listed groups are added (the contact stays in its other groups). Rows with errors are
/// never saved. Missing categories and groups are created.
/// </summary>
public sealed record ImportContactsCommand(Stream File, string FileName, bool Apply) : ICommand<ImportResultDto>;

internal sealed class ImportContactsCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IContactSpreadsheet spreadsheet, TimeProvider clock)
    : ICommandHandler<ImportContactsCommand, ImportResultDto>
{
    public const int MaxRows = 5000;

    private static readonly string[] DefaultColors = ["#10b981", "#3b82f6", "#f59e0b", "#ef4444", "#8b5cf6", "#ec4899", "#14b8a6", "#64748b"];

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
        var categories = await db.Categories.Where(c => c.UserId == userId).ToListAsync(cancellationToken);
        var groups = await db.ContactGroups.Where(g => g.UserId == userId).ToListAsync(cancellationToken);
        var categoryByName = categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var groupByName = groups.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);
        var existing = await db.Contacts.Include(c => c.Groups).Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.PhoneNumber, cancellationToken);

        var parsed = new List<ParsedRow>();
        var seenPhones = new Dictionary<string, int>();
        foreach (var row in read.Rows)
        {
            var p = Parse(row, defaultLanguage);
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

        var newCategories = parsed.Where(p => p.Errors.Count == 0 && p.Category is not null && !categoryByName.ContainsKey(p.Category))
            .Select(p => p.Category!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var newGroups = parsed.Where(p => p.Errors.Count == 0).SelectMany(p => p.Groups)
            .Where(g => !groupByName.ContainsKey(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
        var colorIndex = categories.Count;
        foreach (var name in newCategories)
        {
            var category = new Category(userId, name, DefaultColors[colorIndex++ % DefaultColors.Length], now);
            db.Categories.Add(category);
            categoryByName[name] = category;
        }

        foreach (var name in newGroups)
        {
            var group = new ContactGroup(userId, name, now);
            db.ContactGroups.Add(group);
            groupByName[name] = group;
        }

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
                        p.Salutation ?? contact.Salutation,
                        p.Birthday ?? (contact.BirthDay is { } d && contact.BirthMonth is { } m ? new Birthday(d, m, contact.BirthYear) : null),
                        p.Notes ?? contact.Notes),
                    now);
            }
            else
            {
                contact = new Contact(userId, p.Phone!, now);
                contact.Update(
                    new ContactDetails(p.Phone!, p.SavedName, p.NameEn, p.NameHi, p.NameGu, p.Language, categoryId, p.Salutation, p.Birthday, p.Notes),
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

        // One save: the whole file is imported, or (on any database error) nothing is.
        await db.SaveChangesAsync(cancellationToken);
        return new ImportResultDto(true, created, updated, failed, newCategories, newGroups, rows);
    }

    private static ParsedRow Parse(SpreadsheetRow row, ContactLanguage defaultLanguage)
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
        p.Salutation = Limit(Cell(ImportColumns.Salutation), Contact.SalutationMaxLength, "Salutation", p.Errors);
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
        }
        else
        {
            p.Errors.Add("Language: use en, hi or gu.");
        }

        if (Cell(ImportColumns.Birthday) is { } birthdayText)
        {
            p.Birthday = ParseBirthday(birthdayText);
            if (p.Birthday is null)
            {
                p.Errors.Add("Birthday: use DD-MM or DD-MM-YYYY with a real date.");
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

    private static string? Limit(string? value, int max, string column, List<string> errors)
    {
        if (value is not null && value.Length > max)
        {
            errors.Add($"{column}: at most {max} characters.");
        }

        return value;
    }

    /// <summary>DD-MM or DD-MM-YYYY; "/" and "." also work as separators.</summary>
    internal static Birthday? ParseBirthday(string text)
    {
        var parts = text.Split(['-', '/', '.'], StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month))
        {
            return null;
        }

        int? year = null;
        if (parts.Length == 3)
        {
            if (parts[2].Length != 4 || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var y))
            {
                return null;
            }

            year = y;
        }

        return Birthday.Create(day, month, year);
    }

    private sealed class ParsedRow
    {
        public int RowNumber { get; init; }

        public string? RawPhone { get; init; }

        public string? Phone { get; set; }

        public string? SavedName { get; set; }

        public string? NameEn { get; set; }

        public string? NameHi { get; set; }

        public string? NameGu { get; set; }

        public string? Category { get; set; }

        public ContactLanguage Language { get; set; }

        public string? Salutation { get; set; }

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
