using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts;

public sealed record CategoryDto(Guid Id, string Name, string Color, int ContactCount);

public sealed record GroupDto(Guid Id, string Name, int ContactCount);

public sealed record GroupRefDto(Guid Id, string Name);

/// <summary>One of the contact's occasion dates (Birthday, Anniversary, Asthi, ...): the user's occasion and its date.</summary>
/// <param name="Year">Needed for one-time occasions; optional for yearly ones and birthdays.</param>
/// <param name="ForName">The second name, for occasions that have one (used as {{for_name}}).</param>
public sealed record ContactOccasionDto(Guid ModuleId, int Day, int Month, int? Year, string? ForName);

public sealed record ContactDto(
    Guid Id,
    string PhoneNumber,
    string? SavedName,
    string? NameEn,
    string? NameHi,
    string? NameGu,
    string Language,
    Guid? CategoryId,
    string? Salutation,
    Guid? SalutationId,
    IReadOnlyList<ContactOccasionDto> Occasions,
    IReadOnlyDictionary<Guid, string> Fields,
    string? Notes,
    IReadOnlyList<GroupRefDto> Groups,
    bool NeedsDetails,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? OptedOutAt,
    string WhatsAppCheck,
    DateTimeOffset? WhatsAppCheckedAt)
{
    /// <summary>Use <see cref="ContactReads.ToDtosAsync"/>: it loads the occasion dates and field values.</summary>
    internal static ContactDto From(Contact c, IReadOnlyList<ContactOccasionDto> occasions, IReadOnlyDictionary<Guid, string> fields) => new(
        c.Id,
        c.PhoneNumber,
        c.SavedName,
        c.NameEn,
        c.NameHi,
        c.NameGu,
        ContactLanguages.ToCode(c.Language),
        c.CategoryId,
        c.Salutation,
        c.SalutationId,
        occasions,
        fields,
        c.Notes,
        c.Groups.OrderBy(g => g.Name).Select(g => new GroupRefDto(g.Id, g.Name)).ToList(),
        string.IsNullOrEmpty(c.NameIn(c.Language)),
        c.CreatedAt,
        c.UpdatedAt,
        c.OptedOutAt,
        WhatsAppChecks.ToCode(c.WhatsAppCheck),
        c.WhatsAppCheckedAt);
}

public sealed record ContactPageDto(IReadOnlyList<ContactDto> Items, int Total, int Page, int PageSize);

/// <summary>The fields of the add/edit form. Groups are set by id.</summary>
/// <summary>A contact as the form or the app sends it.</summary>
/// <param name="Occasions">
/// All of the contact's occasion dates (only the user's own occasions). Null leaves them as they are; an empty list
/// removes them all.
/// </param>
/// <param name="Fields">Custom field values by field id. Null leaves them; an empty or blank value removes that one.</param>
public sealed record ContactInput(
    string PhoneNumber,
    string? SavedName,
    string? NameEn,
    string? NameHi,
    string? NameGu,
    string Language,
    Guid? CategoryId,
    Guid? SalutationId,
    IReadOnlyList<ContactOccasionDto>? Occasions,
    string? Notes,
    IReadOnlyList<Guid>? GroupIds,
    IReadOnlyDictionary<Guid, string?>? Fields = null);
