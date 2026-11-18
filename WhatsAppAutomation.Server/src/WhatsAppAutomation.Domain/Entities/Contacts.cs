namespace WhatsAppAutomation.Domain.Entities;

/// <summary>The languages a message can be written in. Stored as "en", "hi" or "gu".</summary>
public enum ContactLanguage
{
    En,
    Hi,
    Gu,
}

/// <summary>Whether a contact's number has WhatsApp, as far as we know.</summary>
public enum WhatsAppCheck
{
    /// <summary>Never checked (contacts saved before the check existed). Checked when the user asks.</summary>
    Unchecked,

    /// <summary>Waiting to be checked: done automatically as soon as the user's WhatsApp is connected.</summary>
    Pending,

    OnWhatsApp,

    /// <summary>Not on WhatsApp: kept apart, cannot be in groups and gets no messages.</summary>
    NotOnWhatsApp,
}

/// <summary>
/// A user's own grouping of contacts by relationship, for example "Elders" or "Youngers".
/// A contact has at most one category (it may have none). Each user has their own categories.
/// </summary>
public sealed class Category
{
    public const int NameMaxLength = 50;
    public const int ColorMaxLength = 7;

    private Category()
    {
    }

    public Category(Guid userId, string name, string color, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CreatedAt = now;
        Update(name, color);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>A colour such as "#10b981", shown as the contact's flag.</summary>
    public string Color { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string name, string color)
    {
        Name = name.Trim();
        Color = color.Trim().ToLowerInvariant();
    }
}

/// <summary>A named list of contacts, for example "Family – Surat". A contact can be in many groups.</summary>
public sealed class ContactGroup
{
    public const int NameMaxLength = 80;

    private ContactGroup()
    {
    }

    public ContactGroup(Guid userId, string name, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CreatedAt = now;
        Rename(name);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public List<Contact> Contacts { get; private set; } = [];

    public void Rename(string name) => Name = name.Trim();
}

/// <summary>
/// One person a user can message. The saved name is only how the user knows them ("Daddy"); messages
/// use the real name in the language they are sent in ("Rameshbhai" / "रमेशभाई" / "રમેશભાઈ").
/// The phone number is unique per user.
/// </summary>
public sealed class Contact
{
    public const int NameMaxLength = 100;
    public const int SalutationMaxLength = 50;
    public const int NotesMaxLength = 1000;

    private Contact()
    {
    }

    public Contact(Guid userId, string phoneNumber, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        PhoneNumber = phoneNumber;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>E.164, for example "+919876543210".</summary>
    public string PhoneNumber { get; private set; } = string.Empty;

    public string? SavedName { get; private set; }

    public string? NameEn { get; private set; }

    public string? NameHi { get; private set; }

    public string? NameGu { get; private set; }

    public ContactLanguage Language { get; private set; }

    /// <summary>Optional: a contact can be without a category.</summary>
    public Guid? CategoryId { get; private set; }

    public string? Salutation { get; private set; }

    /// <summary>
    /// The birthday kept on the contact: day and month, the year optional. The Birthday occasion's dates are the ones
    /// used and shown; this copy keeps a birthday when the user has no Birthday occasion (or deleted it), so a new
    /// Birthday occasion starts with it.
    /// </summary>
    public int? BirthDay { get; private set; }

    public int? BirthMonth { get; private set; }

    public int? BirthYear { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// When the user marked this person as not wanting broadcasts (for example they asked in person). Null when
    /// they may get broadcasts. Broadcasts skip them; a single message sent by hand still works.
    /// </summary>
    public DateTimeOffset? OptedOutAt { get; private set; }

    public List<ContactGroup> Groups { get; private set; } = [];

    /// <summary>Whether the number has WhatsApp. New contacts start as Pending and are checked automatically.</summary>
    public WhatsAppCheck WhatsAppCheck { get; private set; } = WhatsAppCheck.Pending;

    public DateTimeOffset? WhatsAppCheckedAt { get; private set; }

    public bool IsNotOnWhatsApp => WhatsAppCheck == WhatsAppCheck.NotOnWhatsApp;

    /// <summary>Queues the number for a (new) WhatsApp check.</summary>
    public void RequestWhatsAppCheck()
    {
        WhatsAppCheck = WhatsAppCheck.Pending;
    }

    /// <summary>
    /// Saves the result of a WhatsApp check. A number without WhatsApp leaves all its groups (load
    /// <see cref="Groups"/> first). Its category stays: that is only a label.
    /// </summary>
    public void RecordWhatsAppCheck(bool onWhatsApp, DateTimeOffset now)
    {
        WhatsAppCheck = onWhatsApp ? WhatsAppCheck.OnWhatsApp : WhatsAppCheck.NotOnWhatsApp;
        WhatsAppCheckedAt = now;
        if (!onWhatsApp)
        {
            Groups.Clear();
        }
    }

    /// <summary>Puts the contact in a category, or in none (null).</summary>
    public void SetCategory(Guid? categoryId, DateTimeOffset now)
    {
        if (CategoryId != categoryId)
        {
            CategoryId = categoryId;
            UpdatedAt = now;
        }
    }

    /// <summary>Marks the contact as not wanting broadcasts. Keeps the first time if already opted out.</summary>
    public void OptOut(DateTimeOffset now)
    {
        if (OptedOutAt is null)
        {
            OptedOutAt = now;
            UpdatedAt = now;
        }
    }

    /// <summary>The person agreed to get broadcasts again.</summary>
    public void AllowBroadcasts(DateTimeOffset now)
    {
        if (OptedOutAt is not null)
        {
            OptedOutAt = null;
            UpdatedAt = now;
        }
    }

    public void Update(ContactDetails details, DateTimeOffset now)
    {
        if (PhoneNumber != details.PhoneNumber)
        {
            // A new number must be checked again.
            WhatsAppCheck = WhatsAppCheck.Pending;
            WhatsAppCheckedAt = null;
        }

        PhoneNumber = details.PhoneNumber;
        SavedName = Clean(details.SavedName);
        NameEn = Clean(details.NameEn);
        NameHi = Clean(details.NameHi);
        NameGu = Clean(details.NameGu);
        Language = details.Language;
        CategoryId = details.CategoryId;
        Salutation = Clean(details.Salutation);
        if (details.Birthday is { } birthday)
        {
            // Only an import row with a birthday sets it here; the contact form sets dates through occasions.
            SetBirthday(birthday, now);
        }

        Notes = Clean(details.Notes);
        UpdatedAt = now;
    }

    /// <summary>Keeps the birthday on the contact (written with the Birthday occasion's date).</summary>
    public void SetBirthday(Birthday? birthday, DateTimeOffset now)
    {
        if (BirthDay != birthday?.Day || BirthMonth != birthday?.Month || BirthYear != birthday?.Year)
        {
            BirthDay = birthday?.Day;
            BirthMonth = birthday?.Month;
            BirthYear = birthday?.Year;
            UpdatedAt = now;
        }
    }

    /// <summary>The real name in a language, or null when it is missing.</summary>
    public string? NameIn(ContactLanguage language) => language switch
    {
        ContactLanguage.En => NameEn,
        ContactLanguage.Hi => NameHi,
        ContactLanguage.Gu => NameGu,
        _ => null,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Everything about a contact that a form or an import row sets at once.</summary>
public sealed record ContactDetails(
    string PhoneNumber,
    string? SavedName,
    string? NameEn,
    string? NameHi,
    string? NameGu,
    ContactLanguage Language,
    Guid? CategoryId,
    string? Salutation,
    Birthday? Birthday,
    string? Notes);

/// <summary>A birthday; the year may be unknown.</summary>
public sealed record Birthday(int Day, int Month, int? Year)
{
    /// <summary>Checks the date exists. Without a year, 29 February is allowed.</summary>
    public static Birthday? Create(int day, int month, int? year)
    {
        if (month is < 1 or > 12 || day < 1)
        {
            return null;
        }

        if (year is { } y && (y < 1900 || y > 2100))
        {
            return null;
        }

        var maxDay = year is { } knownYear ? DateTime.DaysInMonth(knownYear, month) : DateTime.DaysInMonth(2024, month);
        return day <= maxDay ? new Birthday(day, month, year) : null;
    }
}

/// <summary>Per-user settings that are not part of sign-in, such as the default message language.</summary>
public sealed class UserPreferences
{
    private UserPreferences()
    {
    }

    public UserPreferences(Guid userId, ContactLanguage defaultLanguage)
    {
        UserId = userId;
        DefaultLanguage = defaultLanguage;
    }

    public Guid UserId { get; private set; }

    /// <summary>Used for new contacts, and when a message has no text in a contact's language.</summary>
    public ContactLanguage DefaultLanguage { get; set; }

    public const int MinDailyLimit = 300;
    public const int MaxDailyLimit = 500;

    /// <summary>Most messages a day from the user's number, occasions and broadcasts together (300 to 500).</summary>
    public int DailyLimit { get; private set; } = MinDailyLimit;

    /// <summary>No longer used: the rhythm is the system's own. Kept so saved data stays as it was.</summary>
    public int ChunkSize { get; private set; } = 50;

    /// <summary>No longer used (see <see cref="ChunkSize"/>).</summary>
    public int ChunkRestMinutes { get; private set; } = 15;

    /// <summary>Messages go out from this local time (sending time zone)…</summary>
    public TimeOnly SendFrom { get; private set; } = new(9, 0);

    /// <summary>…until this local time. Equal to <see cref="SendFrom"/> means any time of day.</summary>
    public TimeOnly SendUntil { get; private set; } = new(21, 0);

    /// <summary>No longer used (see <see cref="ChunkSize"/>).</summary>
    public int MinGapSeconds { get; private set; } = 30;

    /// <summary>No longer used (see <see cref="ChunkSize"/>).</summary>
    public int MaxGapSeconds { get; private set; } = 90;

    /// <summary>What the user chooses: how many messages a day, and the hours.</summary>
    public void SetSending(int dailyLimit, TimeOnly sendFrom, TimeOnly sendUntil)
    {
        DailyLimit = dailyLimit;
        SendFrom = sendFrom;
        SendUntil = sendUntil;
    }
}
