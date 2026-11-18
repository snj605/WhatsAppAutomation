using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Modules;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Import;

/// <summary>A custom field as an import column: the header is the field's name.</summary>
internal sealed record FieldColumn(ContactField Field, string Key);

internal static class FieldColumns
{
    /// <summary>The user's fields whose header does not clash with a standard or occasion column.</summary>
    public static async Task<List<FieldColumn>> ForUserAsync(
        IAppDbContext db, Guid userId, IReadOnlyCollection<ModuleColumn> occasions, CancellationToken cancellationToken)
    {
        var fields = await db.ContactFields.AsNoTracking().Where(f => f.UserId == userId).OrderBy(f => f.SortOrder).ToListAsync(cancellationToken);
        var taken = ImportColumns.All.Select(c => c.Key)
            .Concat(occasions.Select(o => o.DateKey))
            .Concat(occasions.Where(o => o.ForNameKey is not null).Select(o => o.ForNameKey!))
            .ToHashSet();
        return [.. fields.Select(f => new FieldColumn(f, ImportColumns.KeyOf(f.Name))).Where(c => !taken.Contains(c.Key))];
    }

    public static string Help(ContactField f) => f.Type switch
    {
        ContactFieldType.Number => $"Your field \"{f.Name}\": a number. In messages: {{{{{f.Key}}}}}.",
        ContactFieldType.Date => $"Your field \"{f.Name}\": a date as DD-MM-YYYY. In messages: {{{{{f.Key}}}}}.",
        ContactFieldType.Choice => $"Your field \"{f.Name}\": one of {string.Join(", ", f.Options)}. In messages: {{{{{f.Key}}}}}.",
        _ => $"Your field \"{f.Name}\". In messages: {{{{{f.Key}}}}}.",
    };
}

/// <summary>One column per group in the Excel file: "Group: Family", with Yes to put the person in it.</summary>
public sealed record GroupColumn(ContactGroup Group, string Header, string Key);

public static class GroupColumns
{
    public const string Yes = "Yes";

    public static IReadOnlyList<GroupColumn> For(IEnumerable<ContactGroup> groups) =>
        [.. groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var header = $"Group: {g.Name}";
            return new GroupColumn(g, header, ImportColumns.KeyOf(header));
        })];

    /// <summary>Yes as picked from the list; a few other ways people mark it are read the same.</summary>
    public static bool IsYes(string text) =>
        text.Trim().ToLowerInvariant() is "yes" or "y" or "✓" or "✔" or "1" or "true" or "હા";
}
