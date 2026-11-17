using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Commands;

/// <summary>Puts contacts in a category (Add) or takes them out of it (they then have no category).</summary>
public sealed record ChangeCategoryMembersCommand(Guid CategoryId, IReadOnlyList<Guid> ContactIds, bool Add) : ICommand<CategoryDto>;

internal sealed class ChangeCategoryMembersCommandValidator : AbstractValidator<ChangeCategoryMembersCommand>
{
    public ChangeCategoryMembersCommandValidator() =>
        RuleFor(c => c.ContactIds).NotEmpty().Must(ids => ids.Count <= 5000).WithMessage("Change at most 5000 contacts at once.");
}

internal sealed class ChangeCategoryMembersCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<ChangeCategoryMembersCommand, CategoryDto>
{
    public async Task<Result<CategoryDto>> Handle(ChangeCategoryMembersCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == command.CategoryId && c.UserId == userId, cancellationToken);
        if (category is null)
        {
            return ContactErrors.CategoryNotFound;
        }

        var ids = command.ContactIds.Distinct().ToList();
        var contacts = await db.Contacts.Where(c => c.UserId == userId && ids.Contains(c.Id)).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var contact in contacts)
        {
            if (command.Add)
            {
                contact.SetCategory(category.Id, now);
            }
            else if (contact.CategoryId == category.Id)
            {
                contact.SetCategory(null, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var count = await db.Contacts.CountAsync(c => c.CategoryId == category.Id, cancellationToken);
        return new CategoryDto(category.Id, category.Name, category.Color, count);
    }
}

/// <summary>Creates a category (Id null) or renames/recolours one of the user's own categories.</summary>
public sealed record SaveCategoryCommand(Guid? Id, string Name, string Color) : ICommand<CategoryDto>;

internal sealed partial class SaveCategoryCommandValidator : AbstractValidator<SaveCategoryCommand>
{
    public SaveCategoryCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Category.NameMaxLength)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Enter a name.");
        RuleFor(c => c.Color).NotEmpty().Matches(HexColor()).WithMessage("Choose a colour such as #10b981.");
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}

internal sealed class SaveCategoryCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<SaveCategoryCommand, CategoryDto>
{
    public async Task<Result<CategoryDto>> Handle(SaveCategoryCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var name = command.Name.Trim();
        var duplicate = await db.Categories.AnyAsync(
            c => c.UserId == userId && c.Id != command.Id && c.Name.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate)
        {
            return ContactErrors.DuplicateCategory;
        }

        Category category;
        if (command.Id is { } id)
        {
            var existing = await db.Categories.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
            if (existing is null)
            {
                return ContactErrors.CategoryNotFound;
            }

            category = existing;
            category.Update(name, command.Color);
        }
        else
        {
            category = new Category(userId, name, command.Color, clock.GetUtcNow());
            db.Categories.Add(category);
        }

        await db.SaveChangesAsync(cancellationToken);
        var count = await db.Contacts.CountAsync(c => c.CategoryId == category.Id, cancellationToken);
        return new CategoryDto(category.Id, category.Name, category.Color, count);
    }
}

/// <summary>Deletes one of the user's categories. Its contacts stay; they just have no category any more.</summary>
public sealed record DeleteCategoryCommand(Guid Id) : ICommand;

internal sealed class DeleteCategoryCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock) : ICommandHandler<DeleteCategoryCommand>
{
    public async Task<Result> Handle(DeleteCategoryCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == command.Id && c.UserId == userId, cancellationToken);
        if (category is null)
        {
            return ContactErrors.CategoryNotFound;
        }

        var now = clock.GetUtcNow();
        await db.Contacts.Where(c => c.UserId == userId && c.CategoryId == category.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CategoryId, (Guid?)null).SetProperty(c => c.UpdatedAt, now), cancellationToken);

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
