using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Broadcasts.Commands;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Modules;

/// <summary>
/// Starts each module's wishes once a day, at the time its owner chose, with the chosen template. People already
/// wished today, who do not want broadcasts or who are not on WhatsApp are left out. The paced sender then sends them
/// one by one, occasions before any broadcast. If WhatsApp is not connected they wait and go out as soon as it is
/// connected again (the apps ask the user to reconnect).
/// </summary>
public sealed class ModuleAutoSender(
    IAppDbContext db,
    IIdentityService identity,
    IPermissionService permissions,
    Notifications.NotificationSender notifications,
    IOptions<BroadcastOptions> options,
    TimeProvider clock,
    ILogger<ModuleAutoSender> logger)
{
    /// <summary>Returns how many modules' wishes were started.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var today = ModuleReads.Today(o, now);
        var due = await db.OccasionModules
            .Where(m => m.AutoSend && m.SendTime != null && m.TemplateId != null && (m.AutoSentOn == null || m.AutoSentOn < today))
            .ToListAsync(cancellationToken);

        var started = 0;
        foreach (var m in due.Where(m => ModuleReads.TimePassedToday(m.SendTime!.Value, o, now)))
        {
            // Turned off, or no longer allowed to send occasion wishes: wait (not marked as done for today).
            if (!await identity.MaySendAsync(m.UserId, cancellationToken) ||
                !(await permissions.GetPermissionsAsync(m.UserId, cancellationToken)).Contains(Permissions.Modules.Manage))
            {
                continue;
            }

            m.MarkAutoSent(today);
            var template = await Templates.TemplateAccess.UsableAsync(db, m.UserId, m.TemplateId!.Value, cancellationToken);
            if (template is null)
            {
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var people = await ModuleDates.AllAsync(db, m, cancellationToken);
            var wished = await WishedTodayAsync(db, m, today, cancellationToken);
            var ids = people
                .Where(p => ModuleDates.Next(m.Kind, p, today) == today && p.Contact.OptedOutAt is null && !wished.Contains(p.Contact.Id))
                .Select(p => p.Contact.Id)
                .ToList();
            if (ids.Count == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var name = $"{m.Name} · {today.ToString("d MMM", CultureInfo.InvariantCulture)}";
            // The template's file goes too; choosing a big video for a template already agreed to send it as a document.
            var input = new BroadcastInput(
                name, null, null, null, null, null, ids, template.Text, AttachmentId: template.AttachmentId, ModuleId: m.Id, SendVideoAsDocument: true);
            var created = await BroadcastCreation.CreateAsync(db, m.UserId, input, o, now, cancellationToken);
            if (created.Error is { } error)
            {
                // Nobody could get it (for example everyone needs details): today is done, tried again tomorrow.
                logger.LogInformation("Automatic wishes of module {ModuleId} not started: {Code}", m.Id, error.Code);
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            started++;
            await notifications.SendAsync(m.UserId, "occasion", $"{m.Name}: {ids.Count} wishes started",
                "They go out one by one, before any broadcast.", cancellationToken);
        }

        return started;
    }

    /// <summary>Contacts already in one of today's wishes of this module that is sending or sent.</summary>
    internal static async Task<HashSet<Guid>> WishedTodayAsync(IAppDbContext db, OccasionModule m, DateOnly today, CancellationToken cancellationToken) =>
        (await db.BroadcastRecipients.AsNoTracking()
            .Where(r => r.ContactId != null && (r.Status == RecipientStatus.Pending || r.Status == RecipientStatus.Sent) &&
                db.Broadcasts.Any(b => b.Id == r.BroadcastId && b.UserId == m.UserId && b.Target == BroadcastTarget.Occasion &&
                    b.TargetId == m.Id && b.TargetDate == today && b.Status != BroadcastStatus.Cancelled))
            .Select(r => r.ContactId!.Value)
            .ToListAsync(cancellationToken)).ToHashSet();
}
