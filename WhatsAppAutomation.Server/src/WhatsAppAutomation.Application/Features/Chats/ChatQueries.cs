using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Broadcasts.Queries;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Chats;

/// <summary>
/// One row of the Chats list. Kind is "group", "category", "birthdays" (today's) or "broadcast" (a one-off send to
/// picked people or everyone). Id is the group, category or broadcast id; null for "birthdays".
/// </summary>
public sealed record ChatListItemDto(
    string Kind,
    Guid? Id,
    string Name,
    string? Color,
    int MemberCount,
    string? LastText,
    DateTimeOffset? LastAt,
    BroadcastSummaryDto? Last);

/// <summary>One message in a chat: a broadcast with its text, file and progress.</summary>
public sealed record ChatMessageDto(BroadcastSummaryDto Summary, string? Text, AttachmentDto? Attachment);

public sealed record TargetChatDto(string Kind, Guid Id, string Name, string? Color, int MemberCount, IReadOnlyList<ChatMessageDto> Messages);

/// <param name="Status">"not_sent", "pending", "sent", "delivered", "read", "failed" or "skipped" for today's wishes.</param>
public sealed record BirthdayPersonDto(Guid ContactId, string Name, string PhoneNumber, int? TurnsAge, bool OptedOut, string Status);

public sealed record BirthdaysChatDto(string Date, IReadOnlyList<BirthdayPersonDto> People, IReadOnlyList<ChatMessageDto> Messages);

internal static class ChatReads
{
    /// <summary>Broadcasts the screens still show: running ones, and finished ones for 5 days.</summary>
    public static IQueryable<Broadcast> Visible(IAppDbContext db, Guid userId, DateTimeOffset now)
    {
        var since = now - Broadcast.VisibleAfterFinish;
        return db.Broadcasts.AsNoTracking().Where(b => b.UserId == userId && (b.FinishedAt == null || b.FinishedAt >= since));
    }

    public static string? TextOf(Broadcast b) => b.Text ?? b.TextEn ?? b.TextGu ?? b.TextHi;

    public static async Task<IReadOnlyList<ChatMessageDto>> MessagesAsync(
        IAppDbContext db, Guid userId, List<Broadcast> broadcasts, CancellationToken cancellationToken)
    {
        if (broadcasts.Count == 0)
        {
            return [];
        }

        var ordered = broadcasts.OrderBy(b => b.CreatedAt).ToList();
        var summaries = await BroadcastReads.SummariesAsync(db, userId, ordered.Select(b => b.Id).ToList(), cancellationToken);
        var fileIds = ordered.Where(b => b.AttachmentId != null).Select(b => b.AttachmentId!.Value).Distinct().ToList();
        var files = fileIds.Count == 0
            ? new Dictionary<Guid, AttachmentDto>()
            : await db.StoredFiles.AsNoTracking().Where(f => fileIds.Contains(f.Id))
                .Select(f => new AttachmentDto(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.Data != null))
                .ToDictionaryAsync(f => f.Id, cancellationToken);
        return ordered.Select((b, i) => new ChatMessageDto(
            summaries[i], TextOf(b), b.AttachmentId is { } id && files.TryGetValue(id, out var f) ? f : null)).ToList();
    }
}

/// <summary>The Chats list: every group and category, and one-off sends still on screen. Birthdays have their own view.</summary>
public sealed record GetChatsQuery : IQuery<IReadOnlyList<ChatListItemDto>>;

internal sealed class GetChatsQueryHandler(IAppDbContext db, ICurrentUser currentUser, IPermissionService permissions, TimeProvider clock)
    : IQueryHandler<GetChatsQuery, IReadOnlyList<ChatListItemDto>>
{
    public async Task<Result<IReadOnlyList<ChatListItemDto>>> Handle(GetChatsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var now = clock.GetUtcNow();
        var visible = await ChatReads.Visible(db, userId, now)
            .Select(b => new { b.Id, b.Target, b.TargetId, b.TargetDate, b.Name, b.Text, b.TextEn, b.TextGu, b.TextHi, b.CreatedAt })
            .ToListAsync(cancellationToken);

        // The newest broadcast of each chat.
        var lastByGroup = visible.Where(b => b.Target == BroadcastTarget.Group).GroupBy(b => b.TargetId!.Value)
            .ToDictionary(g => g.Key, g => g.MaxBy(b => b.CreatedAt)!);
        var lastByCategory = visible.Where(b => b.Target == BroadcastTarget.Category).GroupBy(b => b.TargetId!.Value)
            .ToDictionary(g => g.Key, g => g.MaxBy(b => b.CreatedAt)!);
        var oneOffs = visible.Where(b => b.Target is BroadcastTarget.People or BroadcastTarget.Everyone).ToList();

        var lastIds = lastByGroup.Values.Concat(lastByCategory.Values).Concat(oneOffs).Select(b => b.Id).ToList();

        var summaries = (await BroadcastReads.SummariesAsync(db, userId, lastIds, cancellationToken)).ToDictionary(s => s.Id);
        var texts = visible.ToDictionary(x => x.Id, x => x.Text ?? x.TextEn ?? x.TextGu ?? x.TextHi);
        string? Text(Guid id) => texts.GetValueOrDefault(id);

        var items = new List<ChatListItemDto>();

        // Group and category chats are for members with the Broadcasts switch; everyone sees their single messages.
        var mayBroadcast = (await permissions.GetPermissionsAsync(userId, cancellationToken)).Contains(Permissions.Broadcasts.Send);
        var groups = await db.ContactGroups.AsNoTracking().Where(g => g.UserId == userId && mayBroadcast)
            .Select(g => new { g.Id, g.Name, Count = g.Contacts.Count }).ToListAsync(cancellationToken);
        foreach (var g in groups)
        {
            var last = lastByGroup.GetValueOrDefault(g.Id);
            items.Add(new ChatListItemDto("group", g.Id, g.Name, null, g.Count, last is null ? null : Text(last.Id), last?.CreatedAt,
                last is null ? null : summaries[last.Id]));
        }

        var categories = await db.Categories.AsNoTracking().Where(c => c.UserId == userId && mayBroadcast)
            .Select(c => new { c.Id, c.Name, c.Color, Count = db.Contacts.Count(x => x.CategoryId == c.Id) }).ToListAsync(cancellationToken);
        foreach (var c in categories)
        {
            var last = lastByCategory.GetValueOrDefault(c.Id);
            items.Add(new ChatListItemDto("category", c.Id, c.Name, c.Color, c.Count, last is null ? null : Text(last.Id), last?.CreatedAt,
                last is null ? null : summaries[last.Id]));
        }

        // Birthday wishes are not in the Chats list: they have their own Birthdays view (GET /chats/birthdays).
        foreach (var b in oneOffs)
        {
            var summary = summaries[b.Id];
            items.Add(new ChatListItemDto("broadcast", b.Id, b.Name, null, summary.Counts.Total, Text(b.Id), b.CreatedAt, summary));
        }

        // The most recent activity first, then the rest by name.
        return items
            .OrderByDescending(i => i.LastAt ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>A group's or category's chat: its details and the messages sent to it that are still on screen.</summary>
public sealed record GetTargetChatQuery(BroadcastTarget Kind, Guid Id) : IQuery<TargetChatDto>;

internal sealed class GetTargetChatQueryHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : IQueryHandler<GetTargetChatQuery, TargetChatDto>
{
    public async Task<Result<TargetChatDto>> Handle(GetTargetChatQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        string name;
        string? color = null;
        int members;
        if (query.Kind == BroadcastTarget.Group)
        {
            var g = await db.ContactGroups.AsNoTracking().Where(x => x.Id == query.Id && x.UserId == userId)
                .Select(x => new { x.Name, Count = x.Contacts.Count }).SingleOrDefaultAsync(cancellationToken);
            if (g is null)
            {
                return ContactErrors.GroupNotFound;
            }

            (name, members) = (g.Name, g.Count);
        }
        else if (query.Kind == BroadcastTarget.Category)
        {
            var c = await db.Categories.AsNoTracking().Where(x => x.Id == query.Id && x.UserId == userId)
                .Select(x => new { x.Name, x.Color, Count = db.Contacts.Count(y => y.CategoryId == x.Id) }).SingleOrDefaultAsync(cancellationToken);
            if (c is null)
            {
                return ContactErrors.CategoryNotFound;
            }

            (name, color, members) = (c.Name, c.Color, c.Count);
        }
        else
        {
            return Error.Validation("Only group and category chats exist.");
        }

        var broadcasts = await ChatReads.Visible(db, userId, clock.GetUtcNow())
            .Where(b => b.Target == query.Kind && b.TargetId == query.Id)
            .OrderByDescending(b => b.CreatedAt).Take(100)
            .ToListAsync(cancellationToken);
        var messages = await ChatReads.MessagesAsync(db, userId, broadcasts, cancellationToken);
        return new TargetChatDto(query.Kind == BroadcastTarget.Group ? "group" : "category", query.Id, name, color, members, messages);
    }
}

/// <summary>Today's Birthdays chat: everyone with a birthday today, whether they got wishes yet, and today's wishes.</summary>
public sealed record GetBirthdaysChatQuery : IQuery<BirthdaysChatDto>;

internal sealed class GetBirthdaysChatQueryHandler(IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options, TimeProvider clock)
    : IQueryHandler<GetBirthdaysChatQuery, BirthdaysChatDto>
{
    public async Task<Result<BirthdaysChatDto>> Handle(GetBirthdaysChatQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var today = BroadcastTargets.Today(options.Value, clock.GetUtcNow());
        var people = await BirthdayPeople.TodayAsync(db, userId, today, cancellationToken);
        var broadcasts = await db.Broadcasts.AsNoTracking()
            .Where(b => b.UserId == userId && b.Target == BroadcastTarget.Birthday && b.TargetDate == today)
            .ToListAsync(cancellationToken);
        var broadcastIds = broadcasts.Select(b => b.Id).ToList();

        // Each person's newest wish today, with its ticks.
        var contactIds = people.Select(p => p.Id).ToList();
        var rows = await (
            from r in db.BroadcastRecipients.AsNoTracking()
            where broadcastIds.Contains(r.BroadcastId) && r.ContactId != null && contactIds.Contains(r.ContactId.Value)
            join b in db.Broadcasts on r.BroadcastId equals b.Id
            join m in db.OutgoingMessages on r.OutgoingMessageId equals m.Id into messages
            from m in messages.DefaultIfEmpty()
            select new { ContactId = r.ContactId!.Value, r.Status, Ticks = (OutgoingMessageStatus?)m.Status, b.CreatedAt })
            .ToListAsync(cancellationToken);
        var status = rows.GroupBy(x => x.ContactId).ToDictionary(
            g => g.Key,
            g =>
            {
                var latest = g.MaxBy(x => x.CreatedAt)!;
                return BroadcastStatusText.Of(latest.Status, latest.Ticks);
            });

        var list = people
            .Select(c => new BirthdayPersonDto(
                c.Id, BroadcastPlanner.DisplayName(c), c.PhoneNumber, c.BirthYear is { } y ? today.Year - y : null, c.OptedOutAt != null,
                status.GetValueOrDefault(c.Id, "not_sent")))
            .OrderBy(p => p.Status == "not_sent" ? 0 : 1).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var messagesDto = await ChatReads.MessagesAsync(db, userId, broadcasts, cancellationToken);
        return new BirthdaysChatDto(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), list, messagesDto);
    }
}

internal static class BirthdayPeople
{
    /// <summary>The user's contacts whose birthday is today (29 February counts on 28 February in other years).</summary>
    public static async Task<List<Contact>> TodayAsync(IAppDbContext db, Guid userId, DateOnly today, CancellationToken cancellationToken)
    {
        var leapDayToday = today.Month == 2 && today.Day == 28 && !DateTime.IsLeapYear(today.Year);
        return await db.Contacts.AsNoTracking()
            .Where(c => c.UserId == userId && c.BirthMonth == today.Month && c.WhatsAppCheck != WhatsAppCheck.NotOnWhatsApp &&
                (c.BirthDay == today.Day || (leapDayToday && c.BirthDay == 29)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Contacts already in one of today's birthday wishes that is sending or sent (failed and skipped ones are not).</summary>
    public static async Task<HashSet<Guid>> WishedTodayAsync(IAppDbContext db, Guid userId, DateOnly today, CancellationToken cancellationToken) =>
        (await db.BroadcastRecipients.AsNoTracking()
            .Where(r => r.ContactId != null && (r.Status == RecipientStatus.Pending || r.Status == RecipientStatus.Sent) &&
                db.Broadcasts.Any(b => b.Id == r.BroadcastId && b.UserId == userId && b.Target == BroadcastTarget.Birthday &&
                    b.TargetDate == today && b.Status != BroadcastStatus.Cancelled))
            .Select(r => r.ContactId!.Value)
            .ToListAsync(cancellationToken)).ToHashSet();
}
