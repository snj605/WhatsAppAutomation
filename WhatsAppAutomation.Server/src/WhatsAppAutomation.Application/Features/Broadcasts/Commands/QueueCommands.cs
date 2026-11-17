using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts.Queries;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts.Commands;

/// <summary>Moves a waiting broadcast one place up (sooner) or down in the queue. Occasions always stay first.</summary>
public sealed record MoveBroadcastCommand(Guid Id, bool Up) : ICommand;

internal sealed class MoveBroadcastCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<MoveBroadcastCommand>
{
    public async Task<Result> Handle(MoveBroadcastCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        var queue = await db.Broadcasts
            .Where(b => b.UserId == userId && b.Target != BroadcastTarget.Occasion &&
                (b.Status == BroadcastStatus.Sending || b.Status == BroadcastStatus.Paused))
            .OrderBy(b => b.QueuePosition).ThenBy(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
        var index = queue.FindIndex(b => b.Id == command.Id);
        if (index < 0)
        {
            return await db.Broadcasts.AnyAsync(b => b.Id == command.Id && b.UserId == userId, cancellationToken)
                ? BroadcastErrors.NotMovable
                : BroadcastErrors.NotFound;
        }

        var other = command.Up ? index - 1 : index + 1;
        if (other < 0 || other >= queue.Count)
        {
            return Result.Success();
        }

        (queue[index], queue[other]) = (queue[other], queue[index]);
        for (var i = 0; i < queue.Count; i++)
        {
            queue[i].PlaceInQueue(i + 1);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Puts failed messages of a broadcast back in the queue, because the user asked (there is no automatic retry).
/// Numbers not on WhatsApp are not sent again. Without ids, every failed one is retried.
/// </summary>
public sealed record RetryBroadcastCommand(Guid Id, IReadOnlyList<Guid>? RecipientIds) : ICommand<BroadcastSummaryDto>;

internal sealed class RetryBroadcastCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<RetryBroadcastCommand, BroadcastSummaryDto>
{
    public async Task<Result<BroadcastSummaryDto>> Handle(RetryBroadcastCommand command, CancellationToken cancellationToken)
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

        if (broadcast.Status == BroadcastStatus.Cancelled)
        {
            return BroadcastErrors.Finished;
        }

        var ids = command.RecipientIds;
        var failed = await db.BroadcastRecipients
            .Where(r => r.BroadcastId == broadcast.Id && r.Status == RecipientStatus.Failed &&
                r.Reason != WhatsAppChecks.NotOnWhatsAppReason && (ids == null || ids.Contains(r.Id)) &&
                !db.Contacts.Any(c => c.Id == r.ContactId && c.WhatsAppCheck == WhatsAppCheck.NotOnWhatsApp))
            .ToListAsync(cancellationToken);
        if (failed.Count == 0)
        {
            return BroadcastErrors.NothingToRetry;
        }

        foreach (var r in failed)
        {
            r.Retry();
        }

        broadcast.Reopen();
        await db.SaveChangesAsync(cancellationToken);
        return (await BroadcastReads.SummariesAsync(db, userId, [broadcast.Id], cancellationToken)).Single();
    }
}
