using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Sending;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts.Queries;

/// <summary>One waiting or sending item of the queue, with when its messages are expected to go out.</summary>
/// <param name="Kind">"occasion" (always first) or "broadcast".</param>
/// <param name="Status">"sending" or "paused".</param>
/// <param name="ExpectedStart">When its next message is expected; null while paused or not linked.</param>
/// <param name="ExpectedFinish">When its last message is expected; null while paused or not linked.</param>
public sealed record QueueItemDto(
    Guid Id,
    string Name,
    string Kind,
    string Status,
    string? PauseReason,
    BroadcastCountsDto Counts,
    DateTimeOffset? ExpectedStart,
    DateTimeOffset? ExpectedFinish,
    bool CanMoveUp,
    bool CanMoveDown);

/// <summary>Today's sending: the user's limit, what is sent and left, why it waits, and the queue in order.</summary>
public sealed record QueueDto(
    bool Connected,
    int DailyLimit,
    int SentToday,
    int LeftToday,
    string SendingHours,
    bool InSendingHours,
    DateTimeOffset? NextSendAt,
    string? Waiting,
    IReadOnlyList<QueueItemDto> Items);

public sealed record GetQueueQuery : IQuery<QueueDto>;

internal sealed class GetQueueQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, BroadcastPacing pacing, IOptions<BroadcastOptions> options, TimeProvider clock)
    : IQueryHandler<GetQueueQuery, QueueDto>
{
    public async Task<Result<QueueDto>> Handle(GetQueueQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        var now = clock.GetUtcNow();
        var rules = new PacingRules(options.Value);
        var settings = await UserSending.ForAsync(db, userId, cancellationToken);
        var account = await db.WhatsAppAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
        var startOfDay = rules.StartOfToday(now);
        var sentToday = account is null ? 0 : await db.OutgoingMessages.CountAsync(
            m => m.AccountId == account.Id && m.CreatedAt >= startOfDay && m.Status != OutgoingMessageStatus.Failed, cancellationToken);
        var pace = account is null ? null : await db.SendingPaces.AsNoTracking().SingleOrDefaultAsync(p => p.AccountId == account.Id, cancellationToken);

        var active = await db.Broadcasts.AsNoTracking()
            .Where(b => b.UserId == userId && (b.Status == BroadcastStatus.Sending || b.Status == BroadcastStatus.Paused))
            .Select(b => new { b.Id, b.Target, b.QueuePosition, b.CreatedAt, b.Status })
            .ToListAsync(cancellationToken);
        var ordered = active
            .OrderBy(b => b.Target == BroadcastTarget.Occasion ? 0 : 1)
            .ThenBy(b => b.Target == BroadcastTarget.Occasion ? 0 : b.QueuePosition)
            .ThenBy(b => b.CreatedAt)
            .ToList();
        var summaries = (await BroadcastReads.SummariesAsync(db, userId, [.. ordered.Select(b => b.Id)], cancellationToken))
            .ToDictionary(s => s.Id);

        var connected = account?.CanSend ?? false;
        var plan = new SendPlan(rules, settings, sentToday, pace?.SentSinceRest ?? 0, startOfDay,
            Max(now, pace?.NextSendAt ?? now));
        var broadcasts = ordered.Where(b => b.Target != BroadcastTarget.Occasion).Select(b => b.Id).ToList();
        var items = new List<QueueItemDto>();
        foreach (var b in ordered)
        {
            var s = summaries[b.Id];
            (DateTimeOffset First, DateTimeOffset Last)? times =
                connected && b.Status == BroadcastStatus.Sending && s.Counts.Pending > 0 ? plan.Take(s.Counts.Pending) : null;
            var index = broadcasts.IndexOf(b.Id);
            items.Add(new QueueItemDto(
                b.Id,
                s.Name,
                b.Target == BroadcastTarget.Occasion ? "occasion" : "broadcast",
                s.Status,
                s.PauseReason,
                s.Counts,
                times?.First,
                times?.Last,
                index > 0,
                index >= 0 && index < broadcasts.Count - 1));
        }

        var state = account is null ? null : pacing.Find(account.Id);
        var waiting = account is null ? "WhatsApp is not linked."
            : !connected ? "WhatsApp is not connected. Reconnect it to continue."
            : items.Count == 0 ? null
            : state?.Waiting;
        return new QueueDto(
            connected,
            settings.DailyLimit,
            sentToday,
            Math.Max(0, settings.DailyLimit - sentToday),
            PacingRules.Hours(settings),
            rules.InWindow(settings, now),
            items.Any(i => i.ExpectedStart is not null) ? items.First(i => i.ExpectedStart is not null).ExpectedStart : null,
            waiting,
            items);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}

/// <summary>
/// Walks the queue forward in time the way the sender does (average gap, rest after each chunk, sending hours,
/// daily limit), to tell when each item's messages go out. An estimate: the real gaps are random.
/// </summary>
internal sealed class SendPlan(PacingRules rules, UserSending settings, int sentToday, int sentSinceRest, DateTimeOffset startOfToday, DateTimeOffset from)
{
    private readonly TimeSpan _gap = TimeSpan.FromSeconds((settings.MinGapSeconds + settings.MaxGapSeconds) / 2.0);
    private readonly TimeSpan _rest = TimeSpan.FromMinutes(settings.ChunkRestMinutes);
    private DateTimeOffset _t = from;
    private DateTimeOffset _dayStart = startOfToday;
    private int _leftToday = Math.Max(0, settings.DailyLimit - sentToday);
    private int _sinceRest = sentSinceRest;

    /// <summary>Plans this many messages; returns when the first and the last are expected.</summary>
    public (DateTimeOffset First, DateTimeOffset Last) Take(int count)
    {
        DateTimeOffset? first = null;
        var last = _t;
        for (var i = 0; i < count; i++)
        {
            for (var guard = 0; guard < 10; guard++)
            {
                _t = rules.NextWindowStart(settings, _t);
                var day = rules.StartOfToday(_t);
                if (day > _dayStart)
                {
                    _dayStart = day;
                    _leftToday = settings.DailyLimit;
                    _sinceRest = 0;
                }

                if (_leftToday > 0)
                {
                    break;
                }

                // Today's limit is used up: the next message goes at tomorrow's start time.
                _t = rules.NextWindowStart(settings, _dayStart.AddDays(1));
            }

            first ??= _t;
            last = _t;
            _leftToday--;
            _sinceRest++;
            _t += _gap;
            if (_sinceRest >= settings.ChunkSize)
            {
                _t += _rest;
                _sinceRest = 0;
            }
        }

        return (first ?? _t, last);
    }
}

/// <summary>One person in a broadcast, for the result lists.</summary>
/// <param name="Status">"pending", "sent", "delivered", "read", "failed" or "skipped".</param>
public sealed record RecipientRowDto(
    Guid Id,
    Guid? ContactId,
    string Name,
    string PhoneNumber,
    string Status,
    string? Reason,
    int Attempts,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt);

/// <param name="Summary">The broadcast's name, state and counts, so the results screen needs one call.</param>
public sealed record RecipientsPage(IReadOnlyList<RecipientRowDto> Items, int Total, BroadcastSummaryDto Summary);

/// <summary>
/// The people of one broadcast with one result, a page at a time: Status is "all", "pending", "sent" (handed to
/// WhatsApp, with or without more ticks), "delivered" (reached the phone, read included), "read", "failed" or "skipped".
/// </summary>
public sealed record GetBroadcastRecipientsQuery(Guid BroadcastId, string Status, int Page = 1, int PageSize = 50) : IQuery<RecipientsPage>;

internal sealed class GetBroadcastRecipientsQueryValidator : AbstractValidator<GetBroadcastRecipientsQuery>
{
    private static readonly string[] Statuses = ["all", "pending", "sent", "delivered", "read", "failed", "skipped"];

    public GetBroadcastRecipientsQueryValidator()
    {
        RuleFor(q => q.Status).Must(s => Statuses.Contains(s)).WithMessage("Unknown list.");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class GetBroadcastRecipientsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetBroadcastRecipientsQuery, RecipientsPage>
{
    public async Task<Result<RecipientsPage>> Handle(GetBroadcastRecipientsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        if (!await db.Broadcasts.AnyAsync(b => b.Id == query.BroadcastId && b.UserId == userId, cancellationToken))
        {
            return BroadcastErrors.NotFound;
        }

        var rows =
            from r in db.BroadcastRecipients.AsNoTracking()
            where r.BroadcastId == query.BroadcastId
            join m in db.OutgoingMessages on r.OutgoingMessageId equals m.Id into messages
            from m in messages.DefaultIfEmpty()
            select new { r, Ticks = (OutgoingMessageStatus?)m.Status, SentAt = (DateTimeOffset?)m.SentAt, DeliveredAt = (DateTimeOffset?)m.DeliveredAt, ReadAt = (DateTimeOffset?)m.ReadAt };
        rows = query.Status switch
        {
            "pending" => rows.Where(x => x.r.Status == RecipientStatus.Pending),
            "sent" => rows.Where(x => x.r.Status == RecipientStatus.Sent),
            "delivered" => rows.Where(x => x.r.Status == RecipientStatus.Sent &&
                (x.Ticks == OutgoingMessageStatus.Delivered || x.Ticks == OutgoingMessageStatus.Read)),
            "read" => rows.Where(x => x.r.Status == RecipientStatus.Sent && x.Ticks == OutgoingMessageStatus.Read),
            "failed" => rows.Where(x => x.r.Status == RecipientStatus.Failed),
            "skipped" => rows.Where(x => x.r.Status == RecipientStatus.Skipped),
            _ => rows,
        };

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows.OrderBy(x => x.r.Order)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new RecipientsPage(
            [.. page.Select(x => new RecipientRowDto(
                x.r.Id, x.r.ContactId, x.r.Name, x.r.PhoneNumber, BroadcastStatusText.Of(x.r.Status, x.Ticks), x.r.Reason, x.r.Attempts,
                x.SentAt, x.DeliveredAt, x.ReadAt))],
            total,
            (await BroadcastReads.SummariesAsync(db, userId, [query.BroadcastId], cancellationToken)).Single());
    }
}
