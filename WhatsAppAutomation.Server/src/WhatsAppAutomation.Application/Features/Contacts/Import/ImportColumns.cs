namespace WhatsAppAutomation.Application.Features.Contacts.Import;

/// <summary>The columns of the import file, in template order. Keys are what the reader returns cells by.</summary>
public static class ImportColumns
{
    public const string Phone = "phone";
    public const string SavedName = "saved name";
    public const string NameEn = "name (english)";
    public const string NameHi = "name (hindi)";
    public const string NameGu = "name (gujarati)";
    public const string Category = "category";
    public const string Language = "language";
    public const string Salutation = "salutation";
    public const string Birthday = "birthday";
    public const string Groups = "groups";
    public const string Notes = "notes";

    /// <summary>Header text as shown in the template; a trailing "*" marks a required column.</summary>
    public static readonly IReadOnlyList<(string Key, string Header, string Help)> All =
    [
        (Phone, "Phone*", "With the country code, for example +919876543210. Used to match existing contacts."),
        (SavedName, "Saved name", "How you know them, for example Daddy. Not used in messages."),
        (NameEn, "Name (English)", "Real name used in English messages, for example Rameshbhai."),
        (NameHi, "Name (Hindi)", "Real name used in Hindi messages, for example रमेशभाई."),
        (NameGu, "Name (Gujarati)", "Real name used in Gujarati messages, for example રમેશભાઈ."),
        (Category, "Category", "Pick one of your categories from the list. Empty keeps the existing one."),
        (Language, "Language", "Pick English, Hindi or Gujarati. Empty keeps a saved contact's language; new contacts get your default language."),
        (Salutation, "Salutation", "Pick one of your salutations from the list (both languages, for example Shri / શ્રી). Empty keeps the saved one."),
        (Birthday, "Birthday", "DD-MM, DD-MM-YY or DD-MM-YYYY, for example 15-08 or 15-08-1960."),
        (Groups, "Groups", "Older files only: names of your groups, comma-separated. New files have a Yes column per group."),
        (Notes, "Notes", "Anything else, up to 1000 characters."),
    ];

    public static readonly IReadOnlyList<string> Required = [Phone];

    /// <summary>Lower case, no "*", single spaces: how headers are matched.</summary>
    public static string KeyOf(string header) =>
        string.Join(' ', header.Replace("*", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
