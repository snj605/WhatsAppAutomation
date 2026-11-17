using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts.Commands;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.Contacts.Queries;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts.Queries;

/// <summary>What a broadcast would do, without saving anything: who gets which language, and who is skipped and why.</summary>
public sealed record PreviewBroadcastQuery(BroadcastInput Input) : IQuery<BroadcastPreviewDto>;

internal sealed class PreviewBroadcastQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, IPermissionService permissions, IOptions<BroadcastOptions> options, TimeProvider clock)
    : IQueryHandler<PreviewBroadcastQuery, BroadcastPreviewDto>
{
    public async Task<Result<BroadcastPreviewDto>> Handle(PreviewBroadcastQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        if (!await BroadcastAccess.MaySendAsync(permissions, userId, query.Input, cancellationToken))
        {
            return BroadcastErrors.OnlySingleMessages;
        }

        var now = clock.GetUtcNow();
        var defaultLanguage = await Preferences.DefaultLanguageAsync(db, userId, cancellationToken);
        var plan = await BroadcastPlanner.PlanAsync(db, userId, query.Input, defaultLanguage, options.Value.ContactGapDays, now, cancellationToken);
        var sending = plan.Where(p => p.SkipReason is null).ToList();
        var byLanguage = Enum.GetValues<ContactLanguage>().ToDictionary(
            l => ContactLanguages.ToCode(l), l => sending.Count(p => p.Language == l));
        var samples = BroadcastPlanner.IsSingleText(query.Input)
            ? sending.Take(3).Select(p => new SampleMessageDto("all", p.Name, p.Text!)).ToList()
            : sending.GroupBy(p => p.Language!.Value).OrderBy(g => g.Key)
                .Select(g => new SampleMessageDto(ContactLanguages.ToCode(g.Key), g.First().Name, g.First().Text!)).ToList();
        var skipped = plan.Where(p => p.SkipReason is not null).Take(500)
            .Select(p => new SkippedRecipientDto(p.Contact.Id, p.Name, p.Contact.PhoneNumber, p.SkipReason!)).ToList();

        var account = await db.WhatsAppAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
        var rules = new PacingRules(options.Value);
        var cap = account is null ? options.Value.DailyCap : rules.DailyCap(account, now);
        var days = sending.Count == 0 ? 0 : (int)Math.Ceiling(sending.Count / (double)cap);
        return new BroadcastPreviewDto(plan.Count, sending.Count, byLanguage, skipped, samples, cap, days);
    }
}

/// <summary>The user's broadcasts, newest first, with their progress.</summary>
public sealed record GetBroadcastsQuery : IQuery<IReadOnlyList<BroadcastSummaryDto>>;

internal sealed class GetBroadcastsQueryHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IQueryHandler<GetBroadcastsQuery, IReadOnlyList<BroadcastSummaryDto>>
{
    public async Task<Result<IReadOnlyList<BroadcastSummaryDto>>> Handle(GetBroadcastsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        var visibleSince = clock.GetUtcNow() - Broadcast.VisibleAfterFinish;
        var ids = await db.Broadcasts.Where(b => b.UserId == userId && (b.FinishedAt == null || b.FinishedAt >= visibleSince))
            .OrderByDescending(b => b.CreatedAt).Take(200).Select(b => b.Id)
            .ToListAsync(cancellationToken);
        return (await BroadcastReads.SummariesAsync(db, userId, ids, cancellationToken)).ToList();
    }
}

/// <summary>One broadcast with every recipient and their ticks.</summary>
public sealed record GetBroadcastQuery(Guid Id) : IQuery<BroadcastDetailsDto>;

internal sealed class GetBroadcastQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetBroadcastQuery, BroadcastDetailsDto>
{
    public async Task<Result<BroadcastDetailsDto>> Handle(GetBroadcastQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        var broadcast = await db.Broadcasts.AsNoTracking().SingleOrDefaultAsync(b => b.Id == query.Id && b.UserId == userId, cancellationToken);
        if (broadcast is null)
        {
            return BroadcastErrors.NotFound;
        }

        var rows = await (
            from r in db.BroadcastRecipients.AsNoTracking()
            where r.BroadcastId == broadcast.Id
            join m in db.OutgoingMessages on r.OutgoingMessageId equals m.Id into messages
            from m in messages.DefaultIfEmpty()
            orderby r.Order
            select new { r, Ticks = (OutgoingMessageStatus?)m.Status }).ToListAsync(cancellationToken);

        var recipients = rows.Select(x => new RecipientDto(
            x.r.Id,
            x.r.ContactId,
            x.r.Name,
            x.r.PhoneNumber,
            x.r.Language is { } l ? ContactLanguages.ToCode(l) : null,
            x.r.Text,
            BroadcastStatusText.Of(x.r.Status, x.Ticks),
            x.r.Reason,
            x.r.ProcessedAt)).ToList();
        var summary = (await BroadcastReads.SummariesAsync(db, userId, [broadcast.Id], cancellationToken)).Single();
        var attachment = broadcast.AttachmentId is { } fileId
            ? await db.StoredFiles.AsNoTracking().Where(f => f.Id == fileId)
                .Select(f => new AttachmentDto(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.Data != null))
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        return new BroadcastDetailsDto(summary, broadcast.TextEn, broadcast.TextHi, broadcast.TextGu, recipients, broadcast.Text, attachment);
    }
}

/// <summary>Where the paced sender stands for the user's number: today's count against the limit, and the next send.</summary>
public sealed record GetPacingQuery : IQuery<PacingDto>;

internal sealed class GetPacingQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, BroadcastPacing pacing, IOptions<BroadcastOptions> options, TimeProvider clock)
    : IQueryHandler<GetPacingQuery, PacingDto>
{
    public async Task<Result<PacingDto>> Handle(GetPacingQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        var now = clock.GetUtcNow();
        var rules = new PacingRules(options.Value);
        var account = await db.WhatsAppAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
        if (account is null)
        {
            return new PacingDto(false, 0, options.Value.DailyCap, null, rules.InSendingHours(now), rules.SendingHours, null, "WhatsApp is not linked.");
        }

        var startOfDay = rules.StartOfToday(now);
        var sentToday = await db.OutgoingMessages.CountAsync(
            m => m.AccountId == account.Id && m.CreatedAt >= startOfDay && m.Status != OutgoingMessageStatus.Failed, cancellationToken);
        var state = pacing.Find(account.Id);
        var sending = await db.Broadcasts.AnyAsync(b => b.UserId == userId && b.Status == BroadcastStatus.Sending, cancellationToken);
        return new PacingDto(
            account.CanSend,
            sentToday,
            rules.DailyCap(account, now),
            rules.WarmUpWeek(account, now),
            rules.InSendingHours(now),
            rules.SendingHours,
            sending ? state?.NextSendAt : null,
            sending ? state?.Waiting : null);
    }
}

internal static class BroadcastReads
{
    public static async Task<IReadOnlyList<BroadcastSummaryDto>> SummariesAsync(
        IAppDbContext db, Guid userId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var broadcasts = await db.Broadcasts.AsNoTracking().Where(b => b.UserId == userId && ids.Contains(b.Id)).ToListAsync(cancellationToken);
        var counts = await (
            from r in db.BroadcastRecipients.AsNoTracking()
            where ids.Contains(r.BroadcastId)
            join m in db.OutgoingMessages on r.OutgoingMessageId equals m.Id into messages
            from m in messages.DefaultIfEmpty()
            group new { r.Status, Ticks = (OutgoingMessageStatus?)m.Status } by r.BroadcastId into g
            select new
            {
                BroadcastId = g.Key,
                Total = g.Count(),
                Pending = g.Count(x => x.Status == RecipientStatus.Pending),
                Sent = g.Count(x => x.Status == RecipientStatus.Sent),
                Delivered = g.Count(x => x.Status == RecipientStatus.Sent &&
                    (x.Ticks == OutgoingMessageStatus.Delivered || x.Ticks == OutgoingMessageStatus.Read)),
                Read = g.Count(x => x.Status == RecipientStatus.Sent && x.Ticks == OutgoingMessageStatus.Read),
                Failed = g.Count(x => x.Status == RecipientStatus.Failed),
                Skipped = g.Count(x => x.Status == RecipientStatus.Skipped),
            }).ToDictionaryAsync(x => x.BroadcastId, cancellationToken);

        return ids.Select(id => broadcasts.Single(b => b.Id == id)).Select(b =>
        {
            var c = counts.GetValueOrDefault(b.Id);
            return new BroadcastSummaryDto(
                b.Id,
                b.Name,
                BroadcastStatusText.Of(b.Status),
                b.PauseReason,
                BroadcastStatusText.Languages(b),
                c is null ? new BroadcastCountsDto(0, 0, 0, 0, 0, 0, 0) : new BroadcastCountsDto(c.Total, c.Pending, c.Sent, c.Delivered, c.Read, c.Failed, c.Skipped),
                b.CreatedAt,
                b.FinishedAt);
        }).ToList();
    }
}
