using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Commands;

/// <summary>Creates a group (Id null) or renames one of the user's own groups.</summary>
public sealed record SaveGroupCommand(Guid? Id, string Name) : ICommand<GroupDto>;

internal sealed class SaveGroupCommandValidator : AbstractValidator<SaveGroupCommand>
{
    public SaveGroupCommandValidator() =>
        RuleFor(c => c.Name).NotEmpty().MaximumLength(ContactGroup.NameMaxLength)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Enter a name.");
}

internal sealed class SaveGroupCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<SaveGroupCommand, GroupDto>
{
    public async Task<Result<GroupDto>> Handle(SaveGroupCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var name = command.Name.Trim();
        var duplicate = await db.ContactGroups.AnyAsync(
            g => g.UserId == userId && g.Id != command.Id && g.Name.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate)
        {
            return ContactErrors.DuplicateGroup;
        }

        ContactGroup group;
        if (command.Id is { } id)
        {
            var existing = await db.ContactGroups.SingleOrDefaultAsync(g => g.Id == id && g.UserId == userId, cancellationToken);
            if (existing is null)
            {
                return ContactErrors.GroupNotFound;
            }

            group = existing;
            group.Rename(name);
        }
        else
        {
            group = new ContactGroup(userId, name, clock.GetUtcNow());
            db.ContactGroups.Add(group);
        }

        await db.SaveChangesAsync(cancellationToken);
        var count = await db.ContactGroups.Where(g => g.Id == group.Id).Select(g => g.Contacts.Count).SingleAsync(cancellationToken);
        return new GroupDto(group.Id, group.Name, count);
    }
}

/// <summary>Deletes one of the user's groups. The contacts stay; they only leave the group.</summary>
public sealed record DeleteGroupCommand(Guid Id) : ICommand;

internal sealed class DeleteGroupCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<DeleteGroupCommand>
{
    public async Task<Result> Handle(DeleteGroupCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var group = await db.ContactGroups.SingleOrDefaultAsync(g => g.Id == command.Id && g.UserId == userId, cancellationToken);
        if (group is null)
        {
            return ContactErrors.GroupNotFound;
        }

        db.ContactGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
