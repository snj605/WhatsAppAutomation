namespace WhatsAppAutomation.Domain.Entities;

/// <summary>
/// One of the user's own salutations (master data), in both languages, for example "Shri" and "શ્રી". A contact
/// points to one; {{salutation}} in a message becomes the text in the contact's language.
/// </summary>
public sealed class Salutation
{
    public const int TextMaxLength = 30;
    public const int MaxPerUser = 30;

    private Salutation()
    {
    }

    public Salutation(Guid userId, string nameEn, string nameGu, int sortOrder, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Rename(nameEn, nameGu);
        SortOrder = sortOrder;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string NameEn { get; private set; } = string.Empty;

    public string NameGu { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>How it shows in pick-lists and Excel: "Shri / શ્રી" (one text when both are the same).</summary>
    public string Label => Join(NameEn, NameGu);

    public static string Join(string nameEn, string nameGu) =>
        string.Equals(nameEn, nameGu, StringComparison.Ordinal) ? nameEn : $"{nameEn} / {nameGu}";

    public void Rename(string nameEn, string nameGu)
    {
        NameEn = nameEn.Trim();
        NameGu = nameGu.Trim();
    }

    /// <summary>The text for a message in this language: Gujarati for Gujarati contacts, English otherwise.</summary>
    public string In(ContactLanguage language) => language == ContactLanguage.Gu ? NameGu : NameEn;
}
