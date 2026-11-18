using System.Globalization;
using System.Text;

namespace WhatsAppAutomation.Domain.Entities;

/// <summary>What a custom contact field holds.</summary>
public enum ContactFieldType
{
    Text = 0,
    Number = 1,
    Date = 2,
    Choice = 3,
}

/// <summary>
/// A field the user added to their contacts (for example City, Village or Relation). Each user has their own. Its
/// <see cref="Key"/> is used in messages as {{key}}.
/// </summary>
public sealed class ContactField
{
    public const int NameMaxLength = 40;
    public const int KeyMaxLength = 40;
    public const int OptionMaxLength = 60;
    public const int MaxOptions = 50;
    public const int MaxFields = 30;
    public const int ValueMaxLength = 500;

    /// <summary>Placeholders the app already uses; a field cannot take them.</summary>
    public static readonly IReadOnlySet<string> ReservedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "name", "name_en", "name_hi", "name_gu", "salutation", "for_name", "phone", "years",
    };

    private ContactField()
    {
    }

    public ContactField(Guid userId, string name, ContactFieldType type, IEnumerable<string>? options, int sortOrder, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Type = type;
        SortOrder = sortOrder;
        CreatedAt = now;
        Rename(name);
        SetOptions(options);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Lower-case letters, digits and "_", made from the name: "Native village" is {{native_village}}.</summary>
    public string Key { get; private set; } = string.Empty;

    public ContactFieldType Type { get; private set; }

    /// <summary>The choices of a Choice field, in order; empty for other types.</summary>
    public List<string> Options { get; private set; } = [];

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string name)
    {
        Name = name.Trim();
        Key = KeyOf(Name);
    }

    public void SetOptions(IEnumerable<string>? options) =>
        Options = Type == ContactFieldType.Choice
            ? (options ?? []).Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    /// <summary>The placeholder key for a name; empty when the name has no letters or digits.</summary>
    public static string KeyOf(string name)
    {
        var key = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                key.Append(ch);
            }
            else if (key.Length > 0 && key[^1] != '_')
            {
                key.Append('_');
            }
        }

        var text = key.ToString().Trim('_');
        return text.Length > KeyMaxLength ? text[..KeyMaxLength].TrimEnd('_') : text;
    }

    /// <summary>
    /// Checks a typed value and gives it its stored form: numbers with a dot, dates as yyyy-MM-dd, a choice exactly
    /// as listed. Null means the value is not valid for this field.
    /// </summary>
    public string? Normalize(string value)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.Length > ValueMaxLength)
        {
            return null;
        }

        switch (Type)
        {
            case ContactFieldType.Number:
                var number = text.Replace(",", string.Empty, StringComparison.Ordinal);
                return decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                    ? n.ToString(CultureInfo.InvariantCulture)
                    : null;
            case ContactFieldType.Date:
                string[] formats = ["yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy", "d.M.yyyy", "dd-MM-yyyy", "dd/MM/yyyy"];
                return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year is >= 1900 and <= 2200
                    ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null;
            case ContactFieldType.Choice:
                return Options.FirstOrDefault(o => string.Equals(o, text, StringComparison.OrdinalIgnoreCase));
            default:
                return text;
        }
    }

    /// <summary>How a stored value reads in a message: dates as "5 Mar 2019", the rest as stored.</summary>
    public string Display(string stored) =>
        Type == ContactFieldType.Date &&
        DateOnly.TryParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : stored;
}

/// <summary>One contact's value for one of the user's custom fields, in its stored form.</summary>
public sealed class ContactFieldValue
{
    private ContactFieldValue()
    {
    }

    public ContactFieldValue(Guid fieldId, Guid contactId, string value)
    {
        FieldId = fieldId;
        ContactId = contactId;
        Value = value;
    }

    public Guid FieldId { get; private set; }

    public Guid ContactId { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public void Set(string value) => Value = value;
}
