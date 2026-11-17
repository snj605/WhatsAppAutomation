using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts;

public sealed record CategoryDto(Guid Id, string Name, string Color, int ContactCount);

public sealed record GroupDto(Guid Id, string Name, int ContactCount);

public sealed record GroupRefDto(Guid Id, string Name);

public sealed record BirthdayDto(int Day, int Month, int? Year);

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
    BirthdayDto? Birthday,
    string? Notes,
    IReadOnlyList<GroupRefDto> Groups,
    bool NeedsDetails,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? OptedOutAt,
    string WhatsAppCheck,
    DateTimeOffset? WhatsAppCheckedAt)
{
    public static ContactDto From(Contact c) => new(
        c.Id,
        c.PhoneNumber,
        c.SavedName,
        c.NameEn,
        c.NameHi,
        c.NameGu,
        ContactLanguages.ToCode(c.Language),
        c.CategoryId,
        c.Salutation,
        c.BirthDay is { } d && c.BirthMonth is { } m ? new BirthdayDto(d, m, c.BirthYear) : null,
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
public sealed record ContactInput(
    string PhoneNumber,
    string? SavedName,
    string? NameEn,
    string? NameHi,
    string? NameGu,
    string Language,
    Guid? CategoryId,
    string? Salutation,
    BirthdayDto? Birthday,
    string? Notes,
    IReadOnlyList<Guid>? GroupIds);
