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

/// <summary>The Chats list: every group and category, and one-off sends still on screen. Occasion wishes are shown in their module.</summary>
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

        // Occasion wishes are not in the Chats list: each module shows its own (GET /modules/{id}/days).
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
