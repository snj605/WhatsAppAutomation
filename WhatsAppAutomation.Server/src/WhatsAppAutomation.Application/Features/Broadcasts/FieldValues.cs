using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

/// <summary>
/// The user's custom fields and the values of the people a message goes to, for {{key}} placeholders. Loaded only
/// when a text uses one of the user's field keys.
/// </summary>
internal sealed partial class FieldValues
{
    private readonly Dictionary<string, ContactField> _fields;
    private readonly Dictionary<(Guid FieldId, Guid ContactId), string> _values;

    private FieldValues(Dictionary<string, ContactField> fields, Dictionary<(Guid, Guid), string> values)
    {
        _fields = fields;
        _values = values;
    }

    public static readonly FieldValues None = new([], []);

    public static async Task<FieldValues> LoadAsync(
        IAppDbContext db, Guid userId, IReadOnlyCollection<Guid> contactIds, IEnumerable<string?> texts, CancellationToken cancellationToken)
    {
        var keys = texts.Where(t => t is not null).SelectMany(t => Token().Matches(t!).Select(m => m.Groups[1].Value.ToLowerInvariant()))
            .Distinct().ToList();
        if (keys.Count == 0 || contactIds.Count == 0)
        {
            return None;
        }

        var fields = await db.ContactFields.AsNoTracking().Where(f => f.UserId == userId && keys.Contains(f.Key)).ToListAsync(cancellationToken);
        if (fields.Count == 0)
        {
            return None;
        }

        var fieldIds = fields.Select(f => f.Id).ToList();
        var values = await db.ContactFieldValues.AsNoTracking()
            .Where(v => fieldIds.Contains(v.FieldId) && contactIds.Contains(v.ContactId))
            .ToDictionaryAsync(v => (v.FieldId, v.ContactId), v => v.Value, cancellationToken);
        return new FieldValues(fields.ToDictionary(f => f.Key), values);
    }

    /// <summary>Fills the user's field placeholders for one person. Missing: the first field they have no value in.</summary>
    public string Fill(string text, Guid contactId, ref string? missing)
    {
        if (_fields.Count == 0)
        {
            return text;
        }

        string? first = missing;
        var filled = Token().Replace(text, m =>
        {
            if (!_fields.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var field))
            {
                return m.Value;
            }

            if (_values.TryGetValue((field.Id, contactId), out var value))
            {
                return field.Display(value);
            }

            first ??= field.Name;
            return string.Empty;
        });
        missing = first;
        return filled;
    }

    /// <summary>"{{key}}" with lower-case letters, digits and "_".</summary>
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Token();
}
