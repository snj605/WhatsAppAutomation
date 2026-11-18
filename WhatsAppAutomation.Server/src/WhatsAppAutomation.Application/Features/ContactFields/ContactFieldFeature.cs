using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.ContactFields;

/// <summary>One of the user's own contact fields.</summary>
/// <param name="Key">Used in messages as {{key}}.</param>
/// <param name="Type">"text", "number", "date" or "choice".</param>
/// <param name="Options">The choices of a choice field; empty otherwise.</param>
/// <param name="ContactCount">How many contacts have a value in it.</param>
public sealed record ContactFieldDto(Guid Id, string Name, string Key, string Type, IReadOnlyList<string> Options, int ContactCount);

public static class ContactFieldErrors
{
    public static readonly Error NotSignedIn = new("contact_fields.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error NotFound = new("contact_fields.not_found", "This field does not exist.", ErrorKind.NotFound);

    public static readonly Error NameTaken =
        new("contact_fields.name_taken", "You already have a field with this name.", ErrorKind.Conflict);

    public static readonly Error ReservedName = new(
        "contact_fields.reserved", "This name is already used by the app (name, salutation, for_name, …). Choose another.", ErrorKind.Validation);

    public static readonly Error NoLetters =
        new("contact_fields.no_letters", "The name needs at least one English letter or digit (it becomes {{name}} in messages).", ErrorKind.Validation);

    public static readonly Error TooMany =
        new("contact_fields.too_many", $"You can have at most {ContactField.MaxFields} fields.", ErrorKind.Conflict);

    public static readonly Error ChoicesNeeded =
        new("contact_fields.choices_needed", "A choice field needs at least two choices.", ErrorKind.Validation);

    public static readonly Error ChoiceInUse = new(
        "contact_fields.choice_in_use", "Some contacts have a choice you removed. Change those contacts first.", ErrorKind.Conflict);
}

internal static class ContactFieldCodes
{
    public static string Of(ContactFieldType type) => type.ToString().ToLowerInvariant();

    public static ContactFieldType? Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "text" => ContactFieldType.Text,
        "number" => ContactFieldType.Number,
        "date" => ContactFieldType.Date,
        "choice" => ContactFieldType.Choice,
        _ => null,
    };

    public static async Task<List<ContactFieldDto>> ListAsync(IAppDbContext db, Guid userId, CancellationToken cancellationToken, Guid? only = null)
    {
        var fields = await db.ContactFields.AsNoTracking()
            .Where(f => f.UserId == userId && (only == null || f.Id == only))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Name)
            .ToListAsync(cancellationToken);
        var ids = fields.Select(f => f.Id).ToList();
        var counts = await db.ContactFieldValues.Where(v => ids.Contains(v.FieldId))
            .GroupBy(v => v.FieldId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return [.. fields.Select(f => new ContactFieldDto(f.Id, f.Name, f.Key, Of(f.Type), f.Options, counts.GetValueOrDefault(f.Id)))];
    }

    /// <summary>Checks a name: letters for the key, not one of the app's own placeholders, not used yet.</summary>
    public static async Task<Error?> CheckNameAsync(IAppDbContext db, Guid userId, string name, Guid? self, CancellationToken cancellationToken)
    {
        var key = ContactField.KeyOf(name);
        if (key.Length == 0)
        {
            return ContactFieldErrors.NoLetters;
        }

        if (ContactField.ReservedKeys.Contains(key))
        {
            return ContactFieldErrors.ReservedName;
        }

        return await db.ContactFields.AnyAsync(f => f.UserId == userId && f.Key == key && f.Id != self, cancellationToken)
            ? ContactFieldErrors.NameTaken
            : null;
    }
}

/// <summary>The user's fields, in their order.</summary>
public sealed record GetContactFieldsQuery : IQuery<IReadOnlyList<ContactFieldDto>>;

internal sealed class GetContactFieldsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetContactFieldsQuery, IReadOnlyList<ContactFieldDto>>
{
    public async Task<Result<IReadOnlyList<ContactFieldDto>>> Handle(GetContactFieldsQuery query, CancellationToken cancellationToken) =>
        currentUser.UserId is not { } userId
            ? ContactFieldErrors.NotSignedIn
            : await ContactFieldCodes.ListAsync(db, userId, cancellationToken);
}

public sealed record CreateContactFieldCommand(string Name, string Type, IReadOnlyList<string>? Options) : ICommand<ContactFieldDto>;

internal sealed class CreateContactFieldCommandValidator : AbstractValidator<CreateContactFieldCommand>
{
    public CreateContactFieldCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(ContactField.NameMaxLength);
        RuleFor(c => c.Type).Must(t => ContactFieldCodes.Parse(t) is not null).WithMessage("Choose text, number, date or choice.");
        RuleFor(c => c.Options).Must(o => o is null || o.Count <= ContactField.MaxOptions)
            .WithMessage($"At most {ContactField.MaxOptions} choices.");
        RuleForEach(c => c.Options).MaximumLength(ContactField.OptionMaxLength);
    }
}

internal sealed class CreateContactFieldCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<CreateContactFieldCommand, ContactFieldDto>
{
    public async Task<Result<ContactFieldDto>> Handle(CreateContactFieldCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactFieldErrors.NotSignedIn;
        }

        var orders = await db.ContactFields.Where(f => f.UserId == userId).Select(f => f.SortOrder).ToListAsync(cancellationToken);
        if (orders.Count >= ContactField.MaxFields)
        {
            return ContactFieldErrors.TooMany;
        }

        if (await ContactFieldCodes.CheckNameAsync(db, userId, command.Name, null, cancellationToken) is { } bad)
        {
            return bad;
        }

        var type = ContactFieldCodes.Parse(command.Type)!.Value;
        var field = new ContactField(userId, command.Name, type, command.Options, orders.Count == 0 ? 1 : orders.Max() + 1, clock.GetUtcNow());
        if (type == ContactFieldType.Choice && field.Options.Count < 2)
        {
            return ContactFieldErrors.ChoicesNeeded;
        }

        db.ContactFields.Add(field);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return ContactFieldErrors.NameTaken;
        }

        return (await ContactFieldCodes.ListAsync(db, userId, cancellationToken, field.Id)).Single();
    }
}

/// <summary>Renames a field and changes its choices. The type stays: values already typed keep their meaning.</summary>
public sealed record UpdateContactFieldCommand(Guid Id, string Name, IReadOnlyList<string>? Options) : ICommand<ContactFieldDto>;

internal sealed class UpdateContactFieldCommandValidator : AbstractValidator<UpdateContactFieldCommand>
{
    public UpdateContactFieldCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(ContactField.NameMaxLength);
        RuleFor(c => c.Options).Must(o => o is null || o.Count <= ContactField.MaxOptions)
            .WithMessage($"At most {ContactField.MaxOptions} choices.");
        RuleForEach(c => c.Options).MaximumLength(ContactField.OptionMaxLength);
    }
}

internal sealed class UpdateContactFieldCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateContactFieldCommand, ContactFieldDto>
{
    public async Task<Result<ContactFieldDto>> Handle(UpdateContactFieldCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactFieldErrors.NotSignedIn;
        }

        var field = await db.ContactFields.SingleOrDefaultAsync(f => f.Id == command.Id && f.UserId == userId, cancellationToken);
        if (field is null)
        {
            return ContactFieldErrors.NotFound;
        }

        if (await ContactFieldCodes.CheckNameAsync(db, userId, command.Name, field.Id, cancellationToken) is { } bad)
        {
            return bad;
        }

        if (field.Type == ContactFieldType.Choice)
        {
            var kept = (command.Options ?? []).Select(o => o.Trim()).Where(o => o.Length > 0).ToList();
            if (kept.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            {
                return ContactFieldErrors.ChoicesNeeded;
            }

            // A choice still used by a contact cannot disappear (the contact would hold a value that is not listed).
            var used = await db.ContactFieldValues.Where(v => v.FieldId == field.Id).Select(v => v.Value).Distinct().ToListAsync(cancellationToken);
            if (used.Any(u => !kept.Contains(u, StringComparer.OrdinalIgnoreCase)))
            {
                return ContactFieldErrors.ChoiceInUse;
            }

            field.SetOptions(kept);
        }

        field.Rename(command.Name);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return ContactFieldErrors.NameTaken;
        }

        return (await ContactFieldCodes.ListAsync(db, userId, cancellationToken, field.Id)).Single();
    }
}

/// <summary>Deletes a field and its values on every contact. Messages already sent are not changed.</summary>
public sealed record DeleteContactFieldCommand(Guid Id) : ICommand;

internal sealed class DeleteContactFieldCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<DeleteContactFieldCommand>
{
    public async Task<Result> Handle(DeleteContactFieldCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactFieldErrors.NotSignedIn;
        }

        var deleted = await db.ContactFields.Where(f => f.Id == command.Id && f.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? ContactFieldErrors.NotFound : Result.Success();
    }
}
