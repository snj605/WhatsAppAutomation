using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Queries;

/// <summary>The user's categories, by name, with how many contacts each has.</summary>
public sealed record GetCategoriesQuery : IQuery<IReadOnlyList<CategoryDto>>;

internal sealed class GetCategoriesQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(GetCategoriesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        return await db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Color, db.Contacts.Count(x => x.CategoryId == c.Id)))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>The user's groups, by name, with how many contacts each has.</summary>
public sealed record GetGroupsQuery : IQuery<IReadOnlyList<GroupDto>>;

internal sealed class GetGroupsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetGroupsQuery, IReadOnlyList<GroupDto>>
{
    public async Task<Result<IReadOnlyList<GroupDto>>> Handle(GetGroupsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        return await db.ContactGroups.AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.Name)
            .Select(g => new GroupDto(g.Id, g.Name, g.Contacts.Count))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// One page of the user's contacts, sorted by saved name then number. Search matches the number and
/// every name. NeedsDetails lists only contacts without the real name in their own language.
/// WhatsApp: null leaves out numbers without WhatsApp (they have their own list), "not_on_whatsapp" lists only
/// those, "unchecked" lists the ones not checked yet (or waiting), "on_whatsapp" the checked ones, "all" everyone.
/// </summary>
public sealed record GetContactsQuery(
    string? Search = null,
    Guid? CategoryId = null,
    Guid? GroupId = null,
    bool NeedsDetails = false,
    int Page = 1,
    int PageSize = 25,
    string? WhatsApp = null) : IQuery<ContactPageDto>;

internal sealed class GetContactsQueryValidator : AbstractValidator<GetContactsQuery>
{
    public GetContactsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200);
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.WhatsApp).Must(w => w is null || WhatsAppChecks.Filters.Contains(w))
            .WithMessage("whatsApp must be all, unchecked, on_whatsapp or not_on_whatsapp.");
    }
}

internal sealed class GetContactsQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetContactsQuery, ContactPageDto>
{
    public async Task<Result<ContactPageDto>> Handle(GetContactsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var contacts = db.Contacts.AsNoTracking().Where(c => c.UserId == userId);
        if (query.CategoryId is { } categoryId)
        {
            contacts = contacts.Where(c => c.CategoryId == categoryId);
        }

        if (query.GroupId is { } groupId)
        {
            contacts = contacts.Where(c => c.Groups.Any(g => g.Id == groupId));
        }

        contacts = query.WhatsApp switch
        {
            WhatsAppChecks.AllFilter => contacts,
            WhatsAppChecks.NotOnWhatsAppCode => contacts.Where(c => c.WhatsAppCheck == WhatsAppCheck.NotOnWhatsApp),
            WhatsAppChecks.OnWhatsAppCode => contacts.Where(c => c.WhatsAppCheck == WhatsAppCheck.OnWhatsApp),
            WhatsAppChecks.UncheckedCode => contacts.Where(c => c.WhatsAppCheck == WhatsAppCheck.Unchecked || c.WhatsAppCheck == WhatsAppCheck.Pending),
            _ => contacts.Where(c => c.WhatsAppCheck != WhatsAppCheck.NotOnWhatsApp),
        };

        if (query.NeedsDetails)
        {
            contacts = contacts.Where(c =>
                (c.Language == ContactLanguage.En && c.NameEn == null) ||
                (c.Language == ContactLanguage.Hi && c.NameHi == null) ||
                (c.Language == ContactLanguage.Gu && c.NameGu == null));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            var digits = new string(term.Where(char.IsAsciiDigit).ToArray());
            var hasDigits = digits.Length > 0;
            contacts = contacts.Where(c =>
                (hasDigits && c.PhoneNumber.Contains(digits)) ||
                (c.SavedName != null && c.SavedName.ToLower().Contains(term)) ||
                (c.NameEn != null && c.NameEn.ToLower().Contains(term)) ||
                (c.NameHi != null && c.NameHi.Contains(term)) ||
                (c.NameGu != null && c.NameGu.Contains(term)));
        }

        var total = await contacts.CountAsync(cancellationToken);
        var items = await contacts
            .Include(c => c.Groups)
            .OrderBy(c => c.SavedName ?? c.NameEn ?? c.NameGu ?? c.NameHi ?? c.PhoneNumber)
            .ThenBy(c => c.PhoneNumber)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new ContactPageDto(items.Select(ContactDto.From).ToList(), total, query.Page, query.PageSize);
    }
}

/// <summary>One of the user's contacts.</summary>
public sealed record GetContactQuery(Guid Id) : IQuery<ContactDto>;

internal sealed class GetContactQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetContactQuery, ContactDto>
{
    public async Task<Result<ContactDto>> Handle(GetContactQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var contact = await db.Contacts.AsNoTracking().Include(c => c.Groups)
            .SingleOrDefaultAsync(c => c.Id == query.Id && c.UserId == userId, cancellationToken);
        return contact is null ? ContactErrors.ContactNotFound : ContactDto.From(contact);
    }
}
