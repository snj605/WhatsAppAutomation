namespace WhatsAppAutomation.Domain.Entities;

/// <summary>What kind of date an occasion module keeps for each person.</summary>
public enum ModuleKind
{
    /// <summary>Each contact's birthday, every year. Stored like the other occasions (one date per contact).</summary>
    Birthday = 0,

    /// <summary>A day and month per person, every year (for example Asthi visarjan, an anniversary).</summary>
    Yearly = 1,

    /// <summary>One full date per person, once.</summary>
    Once = 2,
}

/// <summary>
/// An occasion a user sends wishes for, such as Birthday or Asthi visarjan. Every user makes their own modules; each
/// has its own template and sending time, and wishes of all modules go before broadcasts.
/// </summary>
public sealed class OccasionModule
{
    public const int NameMaxLength = 50;
    public const int ForNameLabelMaxLength = 50;

    private OccasionModule()
    {
    }

    public OccasionModule(Guid userId, string name, ModuleKind kind, string? forNameLabel, int sortOrder, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Kind = kind;
        SortOrder = sortOrder;
        CreatedAt = now;
        Rename(name, forNameLabel);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ModuleKind Kind { get; private set; }

    /// <summary>
    /// When set, each person's entry also holds a name, shown with this label (for example "In memory of"), and
    /// templates can use it as {{for_name}}. Null: no extra name.
    /// </summary>
    public string? ForNameLabel { get; private set; }

    /// <summary>Order in the menu.</summary>
    public int SortOrder { get; private set; }

    public bool AutoSend { get; private set; }

    /// <summary>Local time (sending time zone) when the day's wishes start, e.g. 07:15.</summary>
    public TimeOnly? SendTime { get; private set; }

    public Guid? TemplateId { get; private set; }

    /// <summary>The last day the automatic wishes were started, so they start only once a day.</summary>
    public DateOnly? AutoSentOn { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string name, string? forNameLabel)
    {
        Name = name.Trim();
        ForNameLabel = string.IsNullOrWhiteSpace(forNameLabel) ? null : forNameLabel.Trim();
    }

    public void SetSending(bool on, TimeOnly? time, Guid? templateId)
    {
        AutoSend = on;
        SendTime = time;
        TemplateId = templateId;
    }

    public void MarkAutoSent(DateOnly day) => AutoSentOn = day;

    /// <summary>Today counts again: the next check starts today's wishes for everyone not wished yet.</summary>
    public void ResetAutoSent() => AutoSentOn = null;
}

/// <summary>One person's date in a Yearly or Once module (Birthday modules use the date on the contact).</summary>
public sealed class ContactOccasion
{
    public const int ForNameMaxLength = 100;

    private ContactOccasion()
    {
    }

    public ContactOccasion(Guid moduleId, Guid contactId)
    {
        ModuleId = moduleId;
        ContactId = contactId;
    }

    public Guid ModuleId { get; private set; }

    public Guid ContactId { get; private set; }

    public int Day { get; private set; }

    public int Month { get; private set; }

    /// <summary>Required for Once modules; optional for Yearly ones (then "years since" can be shown).</summary>
    public int? Year { get; private set; }

    public string? ForName { get; private set; }

    public void Set(int day, int month, int? year, string? forName)
    {
        Day = day;
        Month = month;
        Year = year;
        ForName = string.IsNullOrWhiteSpace(forName) ? null : forName.Trim();
    }
}
