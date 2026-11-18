using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Domain.ValueObjects;

namespace WhatsAppAutomation.Application.Features.Contacts.Commands;

/// <summary>Adds a contact (Id null) or changes one of the user's own contacts.</summary>
public sealed record SaveContactCommand(Guid? Id, ContactInput Input) : ICommand<ContactDto>;

internal sealed class SaveContactCommandValidator : AbstractValidator<SaveContactCommand>
{
    public SaveContactCommandValidator()
    {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.PhoneNumber).NotEmpty().MaximumLength(30);
        RuleFor(c => c.Input.SavedName).MaximumLength(Contact.NameMaxLength);
        RuleFor(c => c.Input.NameEn).MaximumLength(Contact.NameMaxLength);
        RuleFor(c => c.Input.NameHi).MaximumLength(Contact.NameMaxLength);
        RuleFor(c => c.Input.NameGu).MaximumLength(Contact.NameMaxLength);
        RuleFor(c => c.Input.Salutation).MaximumLength(Contact.SalutationMaxLength);
        RuleFor(c => c.Input.Notes).MaximumLength(Contact.NotesMaxLength);
        RuleFor(c => c.Input.Language).Must(l => ContactLanguages.Parse(l) is not null)
            .WithMessage("Language must be en, hi or gu.");
        RuleFor(c => c.Input.CategoryId).Must(id => id != Guid.Empty).When(c => c.Input.CategoryId is not null)
            .WithMessage("The category id is not valid.");
        RuleFor(c => c.Input.GroupIds).Must(g => g is null || g.Count <= 100).WithMessage("A contact can be in at most 100 groups.");
        RuleFor(c => c.Input.Occasions).Must(o => o is null || o.Count <= Modules.ModuleDates.MaxModules)
            .WithMessage($"A contact can have at most {Modules.ModuleDates.MaxModules} occasion dates.");
        RuleFor(c => c.Input.Fields).Must(f => f is null || f.Count <= ContactField.MaxFields)
            .WithMessage($"At most {ContactField.MaxFields} fields.");
    }
}

internal sealed class SaveContactCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<SaveContactCommand, ContactDto>
{
    public async Task<Result<ContactDto>> Handle(SaveContactCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var input = command.Input;
        var details = ContactRules.ToDetails(input);
        if (details.Error is { } invalid)
        {
            return invalid;
        }

        var d = details.Value;
        if (d.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId && c.UserId == userId, cancellationToken))
        {
            return ContactErrors.CategoryNotFound;
        }

        var groupIds = (input.GroupIds ?? []).Distinct().ToList();
        var groups = groupIds.Count == 0
            ? []
            : await db.ContactGroups.Where(g => g.UserId == userId && groupIds.Contains(g.Id)).ToListAsync(cancellationToken);
        if (groups.Count != groupIds.Count)
        {
            return ContactErrors.GroupNotFound;
        }

        if (await db.Contacts.AnyAsync(c => c.UserId == userId && c.PhoneNumber == d.PhoneNumber && c.Id != command.Id, cancellationToken))
        {
            return ContactErrors.DuplicatePhone;
        }

        var now = clock.GetUtcNow();
        Contact contact;
        if (command.Id is { } id)
        {
            var existing = await db.Contacts.Include(c => c.Groups)
                .SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
            if (existing is null)
            {
                return ContactErrors.ContactNotFound;
            }

            contact = existing;
        }
        else
        {
            contact = new Contact(userId, d.PhoneNumber, now);
            db.Contacts.Add(contact);
        }

        contact.Update(d, now);
        if (input.Occasions is { } occasions &&
            await ContactOccasionWrites.ReplaceAsync(db, userId, contact, occasions, now, cancellationToken) is { } badDate)
        {
            return badDate;
        }

        if (input.Fields is { } fields && await ContactFieldWrites.ApplyAsync(db, userId, contact.Id, fields, cancellationToken) is { } badField)
        {
            return badField;
        }

        contact.Groups.RemoveAll(g => !groupIds.Contains(g.Id));
        contact.Groups.AddRange(groups.Where(g => contact.Groups.All(existing => existing.Id != g.Id)));
        if (contact.IsNotOnWhatsApp)
        {
            // A number without WhatsApp cannot be in groups.
            contact.Groups.Clear();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two saves of the same number at the same moment: the unique index decides.
            return ContactErrors.DuplicatePhone;
        }

        return await ContactReads.ToDtoAsync(db, contact, cancellationToken);
    }
}

/// <summary>Deletes some of the user's own contacts. Ids that are not theirs are ignored.</summary>
public sealed record DeleteContactsCommand(IReadOnlyList<Guid> Ids) : ICommand<int>;

internal sealed class DeleteContactsCommandValidator : AbstractValidator<DeleteContactsCommand>
{
    public DeleteContactsCommandValidator() =>
        RuleFor(c => c.Ids).NotEmpty().Must(ids => ids.Count <= 1000).WithMessage("Delete at most 1000 contacts at once.");
}

internal sealed class DeleteContactsCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<DeleteContactsCommand, int>
{
    public async Task<Result<int>> Handle(DeleteContactsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var ids = command.Ids.Distinct().ToList();
        return await db.Contacts.Where(c => c.UserId == userId && ids.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>
/// Marks one of the user's contacts as not wanting broadcasts, or allows them again (for example after a
/// mistaken opt-out when the person asks to get messages again).
/// </summary>
public sealed record SetContactOptOutCommand(Guid Id, bool OptedOut) : ICommand<ContactDto>;

internal sealed class SetContactOptOutCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<SetContactOptOutCommand, ContactDto>
{
    public async Task<Result<ContactDto>> Handle(SetContactOptOutCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var contact = await db.Contacts.Include(c => c.Groups)
            .SingleOrDefaultAsync(c => c.Id == command.Id && c.UserId == userId, cancellationToken);
        if (contact is null)
        {
            return ContactErrors.ContactNotFound;
        }

        var now = clock.GetUtcNow();
        if (command.OptedOut)
        {
            contact.OptOut(now);
        }
        else
        {
            contact.AllowBroadcasts(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ContactReads.ToDtoAsync(db, contact, cancellationToken);
    }
}

/// <summary>Checks shared by the form and the import.</summary>
internal static class ContactRules
{
    public static Result<ContactDetails> ToDetails(ContactInput input)
    {
        if (PhoneNumber.Normalize(input.PhoneNumber) is not { } phone)
        {
            return ContactErrors.InvalidPhoneNumber;
        }

        if (ContactLanguages.Parse(input.Language) is not { } language)
        {
            return Error.Validation("Language must be en, hi or gu.");
        }

        // Dates (Birthday included) come with the occasions, not here.
        var details = new ContactDetails(
            phone, input.SavedName, input.NameEn, input.NameHi, input.NameGu, language, input.CategoryId, input.Salutation, null, input.Notes);
        var name = language switch
        {
            ContactLanguage.Hi => input.NameHi,
            ContactLanguage.Gu => input.NameGu,
            _ => input.NameEn,
        };
        if (string.IsNullOrWhiteSpace(name))
        {
            return ContactErrors.MissingName(ContactLanguages.DisplayName(language));
        }

        return details;
    }
}

/// <summary>Adds some of the user's contacts to one of their groups, or removes them from it.</summary>
public sealed record ChangeGroupMembersCommand(Guid GroupId, IReadOnlyList<Guid> ContactIds, bool Add) : ICommand<GroupDto>;

internal sealed class ChangeGroupMembersCommandValidator : AbstractValidator<ChangeGroupMembersCommand>
{
    public ChangeGroupMembersCommandValidator() =>
        RuleFor(c => c.ContactIds).NotEmpty().Must(ids => ids.Count <= 1000).WithMessage("Choose at most 1000 contacts at once.");
}

internal sealed class ChangeGroupMembersCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<ChangeGroupMembersCommand, GroupDto>
{
    public async Task<Result<GroupDto>> Handle(ChangeGroupMembersCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var ids = command.ContactIds.Distinct().ToList();
        var group = await db.ContactGroups
            .Include(g => g.Contacts.Where(c => ids.Contains(c.Id)))
            .SingleOrDefaultAsync(g => g.Id == command.GroupId && g.UserId == userId, cancellationToken);
        if (group is null)
        {
            return ContactErrors.GroupNotFound;
        }

        if (command.Add)
        {
            var already = group.Contacts.Select(c => c.Id).ToHashSet();
            // Numbers without WhatsApp are left out: they cannot be in groups.
            var toAdd = await db.Contacts.Where(c => c.UserId == userId && ids.Contains(c.Id) && !already.Contains(c.Id) &&
                    c.WhatsAppCheck != WhatsAppCheck.NotOnWhatsApp)
                .ToListAsync(cancellationToken);
            group.Contacts.AddRange(toAdd);
        }
        else
        {
            group.Contacts.Clear();
        }

        await db.SaveChangesAsync(cancellationToken);
        var count = await db.Contacts.CountAsync(c => c.Groups.Any(g => g.Id == group.Id), cancellationToken);
        return new GroupDto(group.Id, group.Name, count);
    }
}
