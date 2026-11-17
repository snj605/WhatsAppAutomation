using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.Sending;
using WhatsAppAutomation.Application.Features.WhatsApp;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

/// <summary>
/// The paced sender. Each run releases at most one message per WhatsApp number, and only when every rule
/// allows it: number connected, inside the user's sending hours, under the user's daily limit, and the random
/// gap since the last message (plus the rest after each chunk) has passed. Occasions go first, then broadcasts in
/// the user's queue order. It pauses broadcasts on the safety stops (blueprint section 2.4).
/// </summary>
public sealed class BroadcastDispatcher(
    IAppDbContext db,
    IWhatsAppGateway gateway,
    IIdentityService identity,
    ISendTurn sendTurn,
    IWhatsAppNotifier notifier,
    BroadcastPacing pacing,
    IOptions<BroadcastOptions> options,
    TimeProvider clock,
    ILogger<BroadcastDispatcher> logger)
{
    private readonly BroadcastOptions _options = options.Value;
    private readonly PacingRules _rules = new(options.Value);

    /// <summary>Returns how many messages were handed to WhatsApp in this run.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        // Free stored files that no unfinished broadcast needs any more, at most once a minute.
        var now = clock.GetUtcNow();
        if (now - pacing.LastFileCleanup >= TimeSpan.FromMinutes(1))
        {
            pacing.LastFileCleanup = now;
            await WhatsAppAutomation.Application.Features.Files.FileCleanup.RunAsync(db, now, cancellationToken);
        }

        var userIds = await db.Broadcasts.Where(b => b.Status == BroadcastStatus.Sending).Select(b => b.UserId).Distinct()
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var userId in userIds)
        {
            if (await RunForUserAsync(userId, cancellationToken))
            {
                sent++;
            }
        }

        return sent;
    }

    private async Task<bool> RunForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // A turned-off user or tenant sends nothing; their messages wait and go out once turned on again.
        if (!await identity.MaySendAsync(userId, cancellationToken))
        {
            return false;
        }

        var now = clock.GetUtcNow();
        var account = await db.WhatsAppAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
        if (account is null || account.Status is WhatsAppConnectionStatus.NotLinked or WhatsAppConnectionStatus.LoggedOut
                or WhatsAppConnectionStatus.Restricted)
        {
            var reason = account?.Status == WhatsAppConnectionStatus.Restricted
                ? "WhatsApp restricted this number. Nothing more is sent."
                : "WhatsApp is not linked. Link the number again, then press Resume.";
            await PauseAllAsync(userId, reason, cancellationToken);
            return false;
        }

        // One sender per number at a time, also across several copies of the API: otherwise two senders could
        // pick the same person, or both send at once and break the pacing and the daily limit.
        await using var turn = await sendTurn.TryTakeAsync(account.Id, cancellationToken);
        if (turn is null)
        {
            return false;
        }

        // Saved at once when new: otherwise every run would push the first send further away.
        var pace = await db.SendingPaces.SingleOrDefaultAsync(p => p.AccountId == account.Id, cancellationToken);
        if (pace is null)
        {
            pace = new SendingPace(account.Id, now);
            db.SendingPaces.Add(pace);
            await db.SaveChangesAsync(cancellationToken);
        }

        // The in-memory copy is only for the pacing screen (next send time and why it waits).
        var state = pacing.For(account.Id, () => new AccountPacing());
        state.NextSendAt = pace.NextSendAt;
        if (!account.CanSend)
        {
            state.Waiting = "Waiting for WhatsApp to connect.";
            return false;
        }

        var settings = await UserSending.ForAsync(db, userId, cancellationToken);
        if (_options.NoWaits)
        {
            settings = settings with { SendFrom = TimeOnly.MinValue, SendUntil = TimeOnly.MinValue, MinGapSeconds = 0, MaxGapSeconds = 0, ChunkRestMinutes = 0 };
        }

        if (!_rules.InWindow(settings, now))
        {
            state.NextSendAt = _rules.NextWindowStart(settings, now);
            state.Waiting = $"Outside your sending hours ({PacingRules.Hours(settings)}). Sending starts at the next start time.";
            return false;
        }

        // Only a note for the user: the pace and the limit stay what the user chose.
        var deliveredRatio = await DeliveredRatioAsync(account.Id, now, cancellationToken);
        var slowNote = deliveredRatio is { } ratio && ratio < _options.MinDeliveredRatio
            ? $"Only {ratio:P0} of recent messages reached the phone. Check that people know your number."
            : null;
        var cap = settings.DailyLimit;
        var startOfDay = _rules.StartOfToday(now);
        var sentToday = await db.OutgoingMessages.CountAsync(
            m => m.AccountId == account.Id && m.CreatedAt >= startOfDay && m.Status != OutgoingMessageStatus.Failed, cancellationToken);
        if (sentToday >= cap)
        {
            state.Waiting = $"Today's limit of {cap} messages is reached. Sending goes on tomorrow.";
            return false;
        }

        if (now < pace.NextSendAt)
        {
            state.Waiting = slowNote;
            return false;
        }

        var recipient = await db.BroadcastRecipients
            .Where(r => r.Status == RecipientStatus.Pending &&
                db.Broadcasts.Any(b => b.Id == r.BroadcastId && b.UserId == userId && b.Status == BroadcastStatus.Sending))
            // Occasion wishes always go first (oldest first), then broadcasts in the user's queue order.
            .OrderBy(r => db.Broadcasts.Where(b => b.Id == r.BroadcastId).Select(b => b.Target == BroadcastTarget.Occasion ? 0 : 1).First())
            .ThenBy(r => db.Broadcasts.Where(b => b.Id == r.BroadcastId)
                .Select(b => b.Target == BroadcastTarget.Occasion ? 0 : b.QueuePosition).First())
            .ThenBy(r => db.Broadcasts.Where(b => b.Id == r.BroadcastId).Select(b => b.CreatedAt).First())
            .ThenBy(r => r.Order)
            .FirstOrDefaultAsync(cancellationToken);
        if (recipient is null)
        {
            await CompleteFinishedAsync(userId, now, cancellationToken);
            state.Waiting = null;
            return false;
        }

        // Checked again right before sending: things may have changed since the broadcast was created.
        if (await SkipReasonAsync(userId, recipient, now, cancellationToken) is { } skip)
        {
            recipient.Skip(skip, now);
            await db.SaveChangesAsync(cancellationToken);
            await CompleteFinishedAsync(userId, now, cancellationToken);
            return false;
        }

        // The broadcast's file, if any: the gateway uploads it to WhatsApp once and reuses it for everyone.
        var file = await db.Broadcasts.AsNoTracking()
            .Where(b => b.Id == recipient.BroadcastId && b.AttachmentId != null)
            .Join(db.StoredFiles, b => b.AttachmentId, f => (Guid?)f.Id, (b, f) => new GatewayFile(f.Id, f.FileName, f.ContentType))
            .SingleOrDefaultAsync(cancellationToken);

        var message = new OutgoingMessage(account.Id, userId, recipient.PhoneNumber, recipient.Text!, now);
        db.OutgoingMessages.Add(message);
        recipient.AttachMessage(message.Id);
        await db.SaveChangesAsync(cancellationToken);

        // Not cancelled with the service: once handed to WhatsApp, the result must be recorded.
        var result = await gateway.SendTextAsync(account.Id, recipient.PhoneNumber, recipient.Text!, message.Id, CancellationToken.None, file);
        var done = clock.GetUtcNow();
        await OutgoingMessageUpdates.SaveWithRetryAsync(db, message, m =>
        {
            if (result.IsSuccess)
            {
                m.MarkSent(result.Value, done);
            }
            else
            {
                m.MarkFailed(result.Error!.Code);
            }

            return true;
        });

        if (result.Error == WhatsAppErrors.GatewayUnavailable || result.Error == WhatsAppErrors.NotConnected)
        {
            // Not this person's fault: try them again later.
            pace.Postpone(done.AddMinutes(1));
            state.NextSendAt = pace.NextSendAt;
            state.Waiting = "The WhatsApp service is not reachable. Trying again in a minute.";
            recipient.DetachMessage();
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning("Broadcast send postponed: {Code}", result.Error.Code);
            return false;
        }

        if (result.IsSuccess)
        {
            recipient.MarkSent(message.Id, done);
            pace.RecordSuccess();
        }
        else
        {
            recipient.Fail(result.Error == WhatsAppErrors.NotOnWhatsApp ? WhatsAppChecks.NotOnWhatsAppReason : result.Error!.Message, done);
            pace.RecordFailure();
        }

        // What the send showed about the number is kept on the contact: without WhatsApp it leaves its groups
        // and is skipped from now on; with WhatsApp it counts as checked.
        if (recipient.ContactId is { } contactId && (result.IsSuccess || result.Error == WhatsAppErrors.NotOnWhatsApp))
        {
            var onWhatsApp = result.IsSuccess;
            var contact = await db.Contacts.Include(c => c.Groups)
                .SingleOrDefaultAsync(c => c.Id == contactId && c.PhoneNumber == recipient.PhoneNumber, CancellationToken.None);
            if (contact is not null && contact.WhatsAppCheck != (onWhatsApp ? WhatsAppCheck.OnWhatsApp : WhatsAppCheck.NotOnWhatsApp))
            {
                contact.RecordWhatsAppCheck(onWhatsApp, done);
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await notifier.MessageChangedAsync(userId, OutgoingMessageDto.From(message), CancellationToken.None);

        if (pace.FailuresInRow >= _options.MaxFailuresInRow)
        {
            await PauseAllAsync(userId, $"{pace.FailuresInRow} messages failed in a row. Check WhatsApp and the numbers, then press Resume.", CancellationToken.None);
            pace.ResetFailures();
        }

        var delay = TimeSpan.FromSeconds(Random.Shared.Next(settings.MinGapSeconds, Math.Max(settings.MinGapSeconds, settings.MaxGapSeconds) + 1));
        if (pace.CountSent(settings.ChunkSize))
        {
            delay += TimeSpan.FromMinutes(settings.ChunkRestMinutes);
        }

        pace.ScheduleNext(done + delay);
        await db.SaveChangesAsync(CancellationToken.None);
        state.NextSendAt = pace.NextSendAt;
        state.Waiting = slowNote;
        await CompleteFinishedAsync(userId, done, CancellationToken.None);
        return result.IsSuccess;
    }

    private async Task<string?> SkipReasonAsync(Guid userId, BroadcastRecipient recipient, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (recipient.ContactId is null)
        {
            return "The contact was deleted.";
        }

        if (string.IsNullOrEmpty(recipient.Text))
        {
            return "No text for this contact.";
        }

        if (await db.Contacts.AnyAsync(c => c.Id == recipient.ContactId && c.WhatsAppCheck == WhatsAppCheck.NotOnWhatsApp, cancellationToken))
        {
            return WhatsAppChecks.NotOnWhatsAppReason;
        }

        // Checked again here: the contact may have been opted out after the broadcast was created.
        if (await db.Contacts.AnyAsync(c => c.Id == recipient.ContactId && c.OptedOutAt != null, cancellationToken))
        {
            return BroadcastPlanner.OptedOutReason;
        }

        if (_options.ContactGapDays > 0)
        {
            var since = now.AddDays(-_options.ContactGapDays);
            var recent = await db.BroadcastRecipients.AnyAsync(
                r => r.Id != recipient.Id && r.PhoneNumber == recipient.PhoneNumber && r.Status == RecipientStatus.Sent && r.ProcessedAt >= since &&
                    db.Broadcasts.Any(b => b.Id == r.BroadcastId && b.UserId == userId),
                cancellationToken);
            if (recent)
            {
                return $"Already got a broadcast in the last {_options.ContactGapDays} days.";
            }
        }

        return null;
    }

    /// <summary>
    /// The share of recent messages (old enough to have arrived) that reached the phone, or null when there
    /// are too few to judge or the check is off. Failed messages are left out: they never left.
    /// </summary>
    private async Task<double?> DeliveredRatioAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_options.MinDeliveredRatio <= 0)
        {
            return null;
        }

        var olderThan = now.AddMinutes(-_options.HealthMinAgeMinutes);
        var notBefore = now.AddDays(-7);
        var statuses = await db.OutgoingMessages.AsNoTracking()
            .Where(m => m.AccountId == accountId && m.CreatedAt <= olderThan && m.CreatedAt >= notBefore &&
                (m.Status == OutgoingMessageStatus.Sent || m.Status == OutgoingMessageStatus.Delivered || m.Status == OutgoingMessageStatus.Read))
            .OrderByDescending(m => m.CreatedAt)
            .Take(_options.HealthSampleSize)
            .Select(m => m.Status)
            .ToListAsync(cancellationToken);
        if (statuses.Count < _options.HealthSampleSize)
        {
            return null;
        }

        return statuses.Count(s => s is OutgoingMessageStatus.Delivered or OutgoingMessageStatus.Read) / (double)statuses.Count;
    }

    private async Task PauseAllAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var sending = await db.Broadcasts.Where(b => b.UserId == userId && b.Status == BroadcastStatus.Sending).ToListAsync(cancellationToken);
        foreach (var broadcast in sending)
        {
            broadcast.Pause(reason);
        }

        if (sending.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Paused {Count} broadcasts: {Reason}", sending.Count, reason);
        }
    }

    private async Task CompleteFinishedAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var finished = await db.Broadcasts
            .Where(b => b.UserId == userId && b.Status == BroadcastStatus.Sending && !b.Recipients.Any(r => r.Status == RecipientStatus.Pending))
            .ToListAsync(cancellationToken);
        foreach (var broadcast in finished)
        {
            broadcast.Complete(now);
        }

        if (finished.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
