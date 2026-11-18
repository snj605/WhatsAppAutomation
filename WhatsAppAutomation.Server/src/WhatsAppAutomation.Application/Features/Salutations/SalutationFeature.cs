using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Salutations;

/// <summary>One of the user's salutations, in both languages.</summary>
/// <param name="Label">How pick-lists and Excel show it: "Shri / શ્રી".</param>
/// <param name="ContactCount">How many contacts have it.</param>
public sealed record SalutationDto(Guid Id, string NameEn, string NameGu, string Label, int ContactCount);

public static class SalutationErrors
{
    public static readonly Error NotSignedIn = new("salutations.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error NotFound = new("salutations.not_found", "This salutation does not exist.", ErrorKind.NotFound);

    public static readonly Error Taken =
        new("salutations.taken", "You already have this salutation.", ErrorKind.Conflict);

    public static readonly Error TooMany =
        new("salutations.too_many", $"You can have at most {Salutation.MaxPerUser} salutations.", ErrorKind.Conflict);

    public static readonly Error HasSlash =
        new("salutations.slash", "Leave out \"/\": it separates the English and Gujarati text in Excel.", ErrorKind.Validation);
}

internal static class SalutationReads
{
    public static async Task<List<SalutationDto>> ListAsync(IAppDbContext db, Guid userId, CancellationToken cancellationToken, Guid? only = null)
    {
        var list = await db.Salutations.AsNoTracking()
            .Where(s => s.UserId == userId && (only == null || s.Id == only))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.NameEn)
            .ToListAsync(cancellationToken);
        var ids = list.Select(s => s.Id).ToList();
        var counts = await db.Contacts.Where(c => c.SalutationId != null && ids.Contains(c.SalutationId.Value))
            .GroupBy(c => c.SalutationId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return [.. list.Select(s => new SalutationDto(s.Id, s.NameEn, s.NameGu, s.Label, counts.GetValueOrDefault(s.Id)))];
    }

    /// <summary>The user's salutations by id, for filling {{salutation}} in messages.</summary>
    public static async Task<IReadOnlyDictionary<Guid, Salutation>> MapAsync(IAppDbContext db, Guid userId, CancellationToken cancellationToken) =>
        await db.Salutations.AsNoTracking().Where(s => s.UserId == userId).ToDictionaryAsync(s => s.Id, cancellationToken);

    /// <summary>
    /// Finds a salutation as a person types or picks it in Excel: the label ("Shri / શ્રી"), or either text alone,
    /// any letter case. Null when it is not in the list.
    /// </summary>
    public static Salutation? Find(IEnumerable<Salutation> list, string typed)
    {
        var t = typed.Trim();
        return list.FirstOrDefault(s =>
            string.Equals(s.Label, t, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.NameEn, t, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.NameGu, t, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Salutation.Join(s.NameEn, s.NameGu).Replace(" ", string.Empty, StringComparison.Ordinal),
                t.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The user's salutations, in their order.</summary>
public sealed record GetSalutationsQuery : IQuery<IReadOnlyList<SalutationDto>>;

internal sealed class GetSalutationsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetSalutationsQuery, IReadOnlyList<SalutationDto>>
{
    public async Task<Result<IReadOnlyList<SalutationDto>>> Handle(GetSalutationsQuery query, CancellationToken cancellationToken) =>
        currentUser.UserId is not { } userId ? SalutationErrors.NotSignedIn : await SalutationReads.ListAsync(db, userId, cancellationToken);
}

/// <summary>Both texts are required: Gujarati contacts get the Gujarati one, the others the English one.</summary>
public sealed record SaveSalutationCommand(Guid? Id, string NameEn, string NameGu) : ICommand<SalutationDto>;

internal sealed class SaveSalutationCommandValidator : AbstractValidator<SaveSalutationCommand>
{
    public SaveSalutationCommandValidator()
    {
        RuleFor(c => c.NameEn).NotEmpty().WithMessage("Type the English text, for example Shri.").MaximumLength(Salutation.TextMaxLength);
        RuleFor(c => c.NameGu).NotEmpty().WithMessage("Type the Gujarati text, for example શ્રી.").MaximumLength(Salutation.TextMaxLength);
    }
}

internal sealed class SaveSalutationCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<SaveSalutationCommand, SalutationDto>
{
    public async Task<Result<SalutationDto>> Handle(SaveSalutationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return SalutationErrors.NotSignedIn;
        }

        var nameEn = command.NameEn.Trim();
        var nameGu = command.NameGu.Trim();
        if (nameEn.Contains('/', StringComparison.Ordinal) || nameGu.Contains('/', StringComparison.Ordinal))
        {
            return SalutationErrors.HasSlash;
        }

        var all = await db.Salutations.Where(s => s.UserId == userId).ToListAsync(cancellationToken);
        if (all.Any(s => s.Id != command.Id && string.Equals(s.NameEn, nameEn, StringComparison.OrdinalIgnoreCase)))
        {
            return SalutationErrors.Taken;
        }

        Salutation salutation;
        if (command.Id is { } id)
        {
            salutation = all.SingleOrDefault(s => s.Id == id) ?? null!;
            if (salutation is null)
            {
                return SalutationErrors.NotFound;
            }

            salutation.Rename(nameEn, nameGu);

            // Contacts keep the English text beside the link (older reads): keep it up to date.
            await db.Contacts.Where(c => c.SalutationId == id)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.Salutation, nameEn), cancellationToken);
        }
        else
        {
            if (all.Count >= Salutation.MaxPerUser)
            {
                return SalutationErrors.TooMany;
            }

            salutation = new Salutation(userId, nameEn, nameGu, all.Count == 0 ? 1 : all.Max(s => s.SortOrder) + 1, clock.GetUtcNow());
            db.Salutations.Add(salutation);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return SalutationErrors.Taken;
        }

        return (await SalutationReads.ListAsync(db, userId, cancellationToken, salutation.Id)).Single();
    }
}

/// <summary>Deletes a salutation; contacts that had it are left without one (their messages skip {{salutation}}).</summary>
public sealed record DeleteSalutationCommand(Guid Id) : ICommand;

internal sealed class DeleteSalutationCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<DeleteSalutationCommand>
{
    public async Task<Result> Handle(DeleteSalutationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return SalutationErrors.NotSignedIn;
        }

        var salutation = await db.Salutations.SingleOrDefaultAsync(s => s.Id == command.Id && s.UserId == userId, cancellationToken);
        if (salutation is null)
        {
            return SalutationErrors.NotFound;
        }

        await db.Contacts.Where(c => c.SalutationId == salutation.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Salutation, (string?)null).SetProperty(c => c.SalutationId, (Guid?)null), cancellationToken);
        db.Salutations.Remove(salutation);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
