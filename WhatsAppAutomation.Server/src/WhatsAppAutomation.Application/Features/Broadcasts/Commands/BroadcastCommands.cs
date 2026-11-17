using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts.Queries;
using WhatsAppAutomation.Application.Features.Contacts.Queries;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts.Commands;

internal sealed class BroadcastInputValidator : AbstractValidator<BroadcastInput>
{
    public BroadcastInputValidator()
    {
        RuleFor(i => i.Name).NotEmpty().MaximumLength(Broadcast.NameMaxLength).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Give the broadcast a name.");
        RuleFor(i => i.Text).MaximumLength(Broadcast.TextMaxLength);
        RuleFor(i => i.AttachmentId).Must(id => id != Guid.Empty).When(i => i.AttachmentId is not null);
        RuleFor(i => i.TextEn).MaximumLength(Broadcast.TextMaxLength);
        RuleFor(i => i.TextHi).MaximumLength(Broadcast.TextMaxLength);
        RuleFor(i => i.TextGu).MaximumLength(Broadcast.TextMaxLength);
        RuleFor(i => i.CategoryIds).Must(l => l is null || l.Count <= 100);
        RuleFor(i => i.GroupIds).Must(l => l is null || l.Count <= 200);
        RuleFor(i => i.ContactIds).Must(l => l is null || l.Count <= 5000);
    }
}

/// <summary>Creates a broadcast and starts it: the paced sender releases its messages one at a time.</summary>
public sealed record CreateBroadcastCommand(BroadcastInput Input) : ICommand<BroadcastSummaryDto>;

internal sealed class CreateBroadcastCommandValidator : AbstractValidator<CreateBroadcastCommand>
{
    public CreateBroadcastCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new BroadcastInputValidator());
}

internal sealed class CreateBroadcastCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options, TimeProvider clock)
    : ICommandHandler<CreateBroadcastCommand, BroadcastSummaryDto>
{
    public async Task<Result<BroadcastSummaryDto>> Handle(CreateBroadcastCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        var created = await BroadcastCreation.CreateAsync(db, userId, command.Input, options.Value, clock.GetUtcNow(), cancellationToken);
        if (created.Error is { } error)
        {
            return error;
        }

        return (await BroadcastReads.SummariesAsync(db, userId, [created.Value], cancellationToken)).Single();
    }
}

/// <summary>Creates and starts a broadcast for a user. Used by the send screens and by the automatic birthday wishes.</summary>
internal static class BroadcastCreation
{
    public static async Task<Result<Guid>> CreateAsync(
        IAppDbContext db, Guid userId, BroadcastInput input, BroadcastOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (BroadcastInputs.Check(input) is { } invalid)
        {
            return invalid;
        }

        var defaultLanguage = await Preferences.DefaultLanguageAsync(db, userId, cancellationToken);
        if (input.AttachmentId is { } fileId &&
            !await db.StoredFiles.AnyAsync(f => f.Id == fileId && f.UserId == userId && f.Data != null, cancellationToken))
        {
            return WhatsAppAutomation.Application.Features.Files.FileErrors.NotFound;
        }

        var plan = await BroadcastPlanner.PlanAsync(db, userId, input, defaultLanguage, options.ContactGapDays, now, cancellationToken);
        if (!plan.Any(p => p.SkipReason is null))
        {
            return BroadcastErrors.NobodyToSend;
        }

        var broadcast = BroadcastPlanner.IsSingleText(input)
            ? new Broadcast(userId, input.Name, null, null, null, now, input.Text)
            : new Broadcast(userId, input.Name, input.TextEn, input.TextHi, input.TextGu, now);
        if (input.AttachmentId is { } attachmentId)
        {
            broadcast.Attach(attachmentId);
        }

        var (target, targetId) = BroadcastTargets.Of(input);
        broadcast.SetTarget(target, targetId, target == BroadcastTarget.Birthday ? BroadcastTargets.Today(options, now) : null);
        db.Broadcasts.Add(broadcast);
        var order = 0;
        foreach (var p in plan)
        {
            var recipient = new BroadcastRecipient(broadcast.Id, p.Contact.Id, p.Contact.PhoneNumber, p.Name, p.Language, p.Text, order++);
            if (p.SkipReason is { } reason)
            {
                recipient.Skip(reason, now);
            }

            db.BroadcastRecipients.Add(recipient);
        }

        await db.SaveChangesAsync(cancellationToken);
        return broadcast.Id;
    }
}

internal static class BroadcastInputs
{
    public static Error? Check(BroadcastInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Text) &&
            string.IsNullOrWhiteSpace(input.TextEn) && string.IsNullOrWhiteSpace(input.TextHi) && string.IsNullOrWhiteSpace(input.TextGu))
        {
            return BroadcastErrors.NoText;
        }

        return !input.AllContacts && (input.CategoryIds?.Count ?? 0) + (input.GroupIds?.Count ?? 0) + (input.ContactIds?.Count ?? 0) == 0
            ? BroadcastErrors.NoRecipients
            : null;
    }
}

public enum BroadcastAction
{
    Pause,
    Resume,
    Cancel,
}

/// <summary>Pauses, resumes or cancels one of the user's broadcasts.</summary>
public sealed record ChangeBroadcastCommand(Guid Id, BroadcastAction Action) : ICommand<BroadcastSummaryDto>;

internal sealed class ChangeBroadcastCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<ChangeBroadcastCommand, BroadcastSummaryDto>
{
    public async Task<Result<BroadcastSummaryDto>> Handle(ChangeBroadcastCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        var broadcast = await db.Broadcasts.SingleOrDefaultAsync(b => b.Id == command.Id && b.UserId == userId, cancellationToken);
        if (broadcast is null)
        {
            return BroadcastErrors.NotFound;
        }

        var now = clock.GetUtcNow();
        switch (command.Action)
        {
            case BroadcastAction.Pause when !broadcast.Pause("Paused by you."):
                return BroadcastErrors.NotPausable;
            case BroadcastAction.Resume when !broadcast.Resume():
                return BroadcastErrors.NotPaused;
            case BroadcastAction.Cancel when !broadcast.Cancel(now):
                return BroadcastErrors.Finished;
            case BroadcastAction.Cancel:
                await db.BroadcastRecipients.Where(r => r.BroadcastId == broadcast.Id && r.Status == RecipientStatus.Pending)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(r => r.Status, RecipientStatus.Skipped)
                            .SetProperty(r => r.Reason, "The broadcast was cancelled.")
                            .SetProperty(r => r.ProcessedAt, now),
                        cancellationToken);
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await BroadcastReads.SummariesAsync(db, userId, [broadcast.Id], cancellationToken)).Single();
    }
}
