using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Modules;

/// <summary>Makes a new occasion module for the signed-in user. It joins the end of the menu.</summary>
/// <param name="Kind">"yearly", "once" or "birthday" (one Birthday per user).</param>
public sealed record CreateModuleCommand(string Name, string Kind, string? ForNameLabel) : ICommand<ModuleDto>;

internal sealed class CreateModuleCommandValidator : AbstractValidator<CreateModuleCommand>
{
    public CreateModuleCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(OccasionModule.NameMaxLength);
        RuleFor(c => c.Kind).Must(k => ModuleDates.ParseKind(k) is not null).WithMessage("Choose every year or once.");
        RuleFor(c => c.ForNameLabel).MaximumLength(OccasionModule.ForNameLabelMaxLength);
    }
}

internal sealed class CreateModuleCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, Platform.AuditLog audit, IOptions<BroadcastOptions> options, TimeProvider clock)
    : ICommandHandler<CreateModuleCommand, ModuleDto>
{
    public async Task<Result<ModuleDto>> Handle(CreateModuleCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ModuleErrors.NotSignedIn;
        }

        var kind = ModuleDates.ParseKind(command.Kind)!.Value;
        var existing = await db.OccasionModules.Where(m => m.UserId == userId)
            .Select(m => new { m.Name, m.Kind, m.SortOrder }).ToListAsync(cancellationToken);
        if (existing.Count >= ModuleDates.MaxModules)
        {
            return ModuleErrors.TooMany;
        }

        if (existing.Any(m => string.Equals(m.Name, command.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return ModuleErrors.NameTaken;
        }

        if (kind == ModuleKind.Birthday && existing.Any(m => m.Kind == ModuleKind.Birthday))
        {
            return ModuleErrors.OneBirthday;
        }

        var now = clock.GetUtcNow();
        var module = new OccasionModule(
            userId, command.Name, kind, kind == ModuleKind.Birthday ? null : command.ForNameLabel,
            existing.Count == 0 ? 1 : existing.Max(m => m.SortOrder) + 1, now);
        db.OccasionModules.Add(module);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("module.created", $"Occasion \"{module.Name}\" created", null, cancellationToken);
        return (await ModuleReads.ListAsync(db, userId, options.Value, now, cancellationToken, module.Id)).Single();
    }
}

/// <summary>Renames a module and changes the label of its second name. Its kind stays.</summary>
public sealed record UpdateModuleCommand(Guid ModuleId, string Name, string? ForNameLabel) : ICommand<ModuleDto>;

internal sealed class UpdateModuleCommandValidator : AbstractValidator<UpdateModuleCommand>
{
    public UpdateModuleCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(OccasionModule.NameMaxLength);
        RuleFor(c => c.ForNameLabel).MaximumLength(OccasionModule.ForNameLabelMaxLength);
    }
}

internal sealed class UpdateModuleCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options, TimeProvider clock)
    : ICommandHandler<UpdateModuleCommand, ModuleDto>
{
    public async Task<Result<ModuleDto>> Handle(UpdateModuleCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ModuleErrors.NotSignedIn;
        }

        var module = await db.OccasionModules.SingleOrDefaultAsync(m => m.Id == command.ModuleId && m.UserId == userId, cancellationToken);
        if (module is null)
        {
            return ModuleErrors.NotFound;
        }

        var name = command.Name.Trim();
        var taken = await db.OccasionModules.AnyAsync(
            m => m.UserId == userId && m.Id != module.Id && m.Name.ToLower() == name.ToLower(), cancellationToken);
        if (taken)
        {
            return ModuleErrors.NameTaken;
        }

        module.Rename(name, module.Kind == ModuleKind.Birthday ? null : command.ForNameLabel);
        await db.SaveChangesAsync(cancellationToken);
        return (await ModuleReads.ListAsync(db, userId, options.Value, clock.GetUtcNow(), cancellationToken, module.Id)).Single();
    }
}

/// <summary>
/// Deletes a module with its people's dates. Wishes already sent stay in the history; ones still waiting are
/// cancelled. Contacts and their birthdays are not touched.
/// </summary>
public sealed record DeleteModuleCommand(Guid ModuleId) : ICommand;

internal sealed class DeleteModuleCommandHandler(IAppDbContext db, ICurrentUser currentUser, Platform.AuditLog audit, TimeProvider clock)
    : ICommandHandler<DeleteModuleCommand>
{
    public async Task<Result> Handle(DeleteModuleCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ModuleErrors.NotSignedIn;
        }

        var module = await db.OccasionModules.SingleOrDefaultAsync(m => m.Id == command.ModuleId && m.UserId == userId, cancellationToken);
        if (module is null)
        {
            return ModuleErrors.NotFound;
        }

        var now = clock.GetUtcNow();
        var waiting = await db.Broadcasts
            .Where(b => b.UserId == userId && b.Target == BroadcastTarget.Occasion && b.TargetId == module.Id &&
                (b.Status == BroadcastStatus.Sending || b.Status == BroadcastStatus.Paused))
            .ToListAsync(cancellationToken);
        foreach (var b in waiting)
        {
            b.Cancel(now);
        }

        db.OccasionModules.Remove(module);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("module.deleted", $"Occasion \"{module.Name}\" deleted", null, cancellationToken);
        return Result.Success();
    }
}

/// <summary>The module's automatic wishes: on or off, the time ("HH:mm") and the template.</summary>
public sealed record SetModuleSendingCommand(Guid ModuleId, bool Enabled, string? Time, Guid? TemplateId) : ICommand<ModuleDto>;

internal sealed class SetModuleSendingCommandValidator : AbstractValidator<SetModuleSendingCommand>
{
    public SetModuleSendingCommandValidator()
    {
        RuleFor(c => c.Time).Must(t => ModuleReads.TryParseTime(t, out _)).When(c => c.Enabled || c.Time is not null)
            .WithMessage("Choose the time for the wishes, like 07:15.");
        RuleFor(c => c.TemplateId).NotNull().When(c => c.Enabled).WithMessage("Choose the template to send.");
    }
}

internal sealed class SetModuleSendingCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options, TimeProvider clock)
    : ICommandHandler<SetModuleSendingCommand, ModuleDto>
{
    public async Task<Result<ModuleDto>> Handle(SetModuleSendingCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ModuleErrors.NotSignedIn;
        }

        var module = await db.OccasionModules.SingleOrDefaultAsync(m => m.Id == command.ModuleId && m.UserId == userId, cancellationToken);
        if (module is null)
        {
            return ModuleErrors.NotFound;
        }

        if (command.TemplateId is { } templateId &&
            await Templates.TemplateAccess.UsableAsync(db, userId, templateId, cancellationToken) is null)
        {
            return ModuleErrors.TemplateNotFound;
        }

        TimeOnly? time = ModuleReads.TryParseTime(command.Time, out var parsed) ? parsed : null;
        var now = clock.GetUtcNow();
        var wasOn = module.AutoSend;
        var oldTime = module.SendTime;
        module.SetSending(command.Enabled, time, command.TemplateId);

        // Switching on (or moving the time) after today's time has passed starts tomorrow, not right now: nobody
        // expects a batch of wishes to go out the moment they save. Today's can still be sent by hand.
        if (command.Enabled && time is { } t && (!wasOn || oldTime != t) && ModuleReads.TimePassedToday(t, options.Value, now))
        {
            module.MarkAutoSent(ModuleReads.Today(options.Value, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await ModuleReads.ListAsync(db, userId, options.Value, now, cancellationToken, module.Id)).Single();
    }
}

/// <summary>Sets one person's date (and second name) in a Yearly or Once module, adding them if needed.</summary>
public sealed record SetModuleEntryCommand(Guid ModuleId, Guid ContactId, int Day, int Month, int? Year, string? ForName) : ICommand;

internal sealed class SetModuleEntryCommandValidator : AbstractValidator<SetModuleEntryCommand>
{
    public SetModuleEntryCommandValidator()
    {
        RuleFor(c => c).Must(c => ModuleDates.IsValid(c.Day, c.Month, c.Year)).WithMessage("This date does not exist.");
        RuleFor(c => c.ForName).MaximumLength(ContactOccasion.ForNameMaxLength);
    }
}

internal sealed class SetModuleEntryCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<SetModuleEntryCommand>
{
    public async Task<Result> Handle(SetModuleEntryCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ModuleErrors.NotSignedIn;
        }

        var module = await db.OccasionModules.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == command.ModuleId && m.UserId == userId, cancellationToken);
        if (module is null)
        {
            return ModuleErrors.NotFound;
        }

        if (module.Kind == ModuleKind.Birthday)
        {
            return ModuleErrors.BirthdayHasNoEntries;
        }

        if (module.Kind == ModuleKind.Once && command.Year is null)
        {
            return Error.Validation("This occasion happens once: enter the full date with the year.");
        }

        if (!await db.Contacts.AnyAsync(c => c.Id == command.ContactId && c.UserId == userId, cancellationToken))
        {
            return ModuleErrors.ContactNotFound;
        }

        var entry = await db.ContactOccasions.SingleOrDefaultAsync(
            o => o.ModuleId == module.Id && o.ContactId == command.ContactId, cancellationToken);
        if (entry is null)
        {
            entry = new ContactOccasion(module.Id, command.ContactId);
            db.ContactOccasions.Add(entry);
        }

        entry.Set(command.Day, command.Month, command.Year, module.ForNameLabel is null ? null : command.ForName);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record RemoveModuleEntryCommand(Guid ModuleId, Guid ContactId) : ICommand;

internal sealed class RemoveModuleEntryCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<RemoveModuleEntryCommand>
{
    public async Task<Result> Handle(RemoveModuleEntryCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ModuleErrors.NotSignedIn;
        }

        if (!await db.OccasionModules.AnyAsync(m => m.Id == command.ModuleId && m.UserId == userId, cancellationToken))
        {
            return ModuleErrors.NotFound;
        }

        await db.ContactOccasions.Where(o => o.ModuleId == command.ModuleId && o.ContactId == command.ContactId)
            .ExecuteDeleteAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>New tenant admins start with a Birthday module (members make their own).</summary>
internal static class DefaultModules
{
    public static async Task AddBirthdayAsync(IAppDbContext db, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!await db.OccasionModules.AnyAsync(m => m.UserId == userId && m.Kind == ModuleKind.Birthday, cancellationToken))
        {
            db.OccasionModules.Add(new OccasionModule(userId, "Birthday", ModuleKind.Birthday, null, 1, now));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
