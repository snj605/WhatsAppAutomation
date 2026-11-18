using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Notifications;

/// <param name="Kind">"whatsapp" (number needs a look), "paused" (sending stopped), "occasion" (wishes started).</param>
public sealed record NotificationDto(Guid Id, string Kind, string Title, string? Body, DateTimeOffset CreatedAt, bool Read)
{
    public static NotificationDto From(Notification n) => new(n.Id, n.Kind, n.Title, n.Body, n.CreatedAt, n.ReadAt is not null);
}

public sealed record NotificationsPage(IReadOnlyList<NotificationDto> Items, int Unread);

/// <summary>Stores a notification and tells the user's open apps at once.</summary>
public sealed class NotificationSender(IAppDbContext db, IWhatsAppNotifier notifier, TimeProvider clock)
{
    public async Task SendAsync(Guid userId, string kind, string title, string? body, CancellationToken cancellationToken)
    {
        var notification = new Notification(userId, kind, title, body, clock.GetUtcNow());
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotificationAsync(userId, NotificationDto.From(notification), cancellationToken);
    }
}

/// <summary>The user's latest 50 notifications and how many are unread.</summary>
public sealed record GetNotificationsQuery : IQuery<NotificationsPage>;

internal sealed class GetNotificationsQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetNotificationsQuery, NotificationsPage>
{
    public async Task<Result<NotificationsPage>> Handle(GetNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return new Error("notifications.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);
        }

        var items = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync(cancellationToken);
        var unread = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);
        return new NotificationsPage([.. items.Select(NotificationDto.From)], unread);
    }
}

/// <summary>Marks the listed notifications, or all when none are listed, as read.</summary>
public sealed record MarkNotificationsReadCommand(IReadOnlyList<Guid>? Ids) : ICommand;

internal sealed class MarkNotificationsReadCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<MarkNotificationsReadCommand>
{
    public async Task<Result> Handle(MarkNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return new Error("notifications.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);
        }

        var ids = command.Ids is { Count: > 0 } list ? list : null;
        var now = clock.GetUtcNow();
        await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null && (ids == null || ids.Contains(n.Id)))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, (DateTimeOffset?)now), cancellationToken);
        return Result.Success();
    }
}
