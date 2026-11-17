using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Broadcasts.Commands;
using WhatsAppAutomation.Application.Features.Chats;
using WhatsAppAutomation.Application.Features.Contacts.Queries;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts;

/// <param name="Time">"HH:mm" in the sending time zone, e.g. "07:15"; null when not set.</param>
/// <param name="NextRunAt">When the next automatic wishes start; null when switched off or not set up.</param>
/// <param name="TimeZone">The time zone the time is in, e.g. "Asia/Kolkata".</param>
public sealed record BirthdaySettingsDto(
    bool Enabled, string? Time, Guid? TemplateId, string? TemplateName, DateTimeOffset? NextRunAt, string TimeZone);

/// <param name="Time">"HH:mm", e.g. "07:15".</param>
public sealed record SetBirthdaySettingsCommand(bool Enabled, string? Time, Guid? TemplateId) : ICommand<BirthdaySettingsDto>;

/// <summary>The user's automatic birthday wishes: on or off, the time and the template.</summary>
public sealed record GetBirthdaySettingsQuery : IQuery<BirthdaySettingsDto>;

internal static class BirthdaySettings
{
    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static async Task<BirthdaySettingsDto> ReadAsync(
        IAppDbContext db, Guid userId, BroadcastOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var p = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var templateName = p?.BirthdayTemplateId is { } id
            ? await db.MessageTemplates.AsNoTracking().Where(t => t.Id == id && t.UserId == userId).Select(t => t.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        var ready = p is { BirthdayAutoSend: true, BirthdaySendTime: not null } && templateName is not null;
        return new BirthdaySettingsDto(
            p?.BirthdayAutoSend ?? false,
            p?.BirthdaySendTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
            templateName is null ? null : p!.BirthdayTemplateId,
            templateName,
            ready ? NextRun(p!, options, now) : null,
            options.TimeZone);
    }

    /// <summary>Today at the set time if today's wishes have not started yet, else tomorrow at that time.</summary>
    public static DateTimeOffset NextRun(UserPreferences p, BroadcastOptions options, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var today = BroadcastTargets.Today(options, now);
        var day = p.BirthdayAutoSentOn is { } done && done >= today ? today.AddDays(1) : today;
        var local = day.ToDateTime(p.BirthdaySendTime!.Value);
        var at = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return at < now ? now : at;
    }

    /// <summary>Whether the set time has passed today in the sending time zone.</summary>
    public static bool TimePassedToday(TimeOnly time, BroadcastOptions options, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone));
        return TimeOnly.FromDateTime(local.DateTime) >= time;
    }
}

internal sealed class GetBirthdaySettingsQueryHandler(IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options, TimeProvider clock)
    : IQueryHandler<GetBirthdaySettingsQuery, BirthdaySettingsDto>
{
    public async Task<Result<BirthdaySettingsDto>> Handle(GetBirthdaySettingsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        return await BirthdaySettings.ReadAsync(db, userId, options.Value, clock.GetUtcNow(), cancellationToken);
    }
}

internal sealed class SetBirthdaySettingsCommandValidator : AbstractValidator<SetBirthdaySettingsCommand>
{
    public SetBirthdaySettingsCommandValidator()
    {
        RuleFor(c => c.Time).Must(t => BirthdaySettings.TryParseTime(t, out _)).When(c => c.Enabled || c.Time is not null)
            .WithMessage("Choose the time for the birthday wishes, like 07:15.");
        RuleFor(c => c.TemplateId).NotNull().When(c => c.Enabled).WithMessage("Choose the template to send.");
    }
}

internal sealed class SetBirthdaySettingsCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options, TimeProvider clock)
    : ICommandHandler<SetBirthdaySettingsCommand, BirthdaySettingsDto>
{
    public async Task<Result<BirthdaySettingsDto>> Handle(SetBirthdaySettingsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        if (command.TemplateId is { } templateId &&
            !await db.MessageTemplates.AnyAsync(t => t.Id == templateId && t.UserId == userId, cancellationToken))
        {
            return Error.Validation("This template does not exist any more. Choose another one.");
        }

        TimeOnly? time = BirthdaySettings.TryParseTime(command.Time, out var parsed) ? parsed : null;
        var now = clock.GetUtcNow();
        var preferences = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (preferences is null)
        {
            preferences = new UserPreferences(userId, await Preferences.DefaultLanguageAsync(db, userId, cancellationToken));
            db.UserPreferences.Add(preferences);
        }

        var wasOn = preferences.BirthdayAutoSend;
        var oldTime = preferences.BirthdaySendTime;
        preferences.SetBirthdayAutoSend(command.Enabled, time, command.TemplateId);

        // Switching on (or moving the time) after today's time has passed starts tomorrow, not right now:
        // nobody expects a batch of wishes to go out the moment they save. Today's can still be sent by hand.
        if (command.Enabled && time is { } t && (!wasOn || oldTime != t) && BirthdaySettings.TimePassedToday(t, options.Value, now))
        {
            preferences.MarkBirthdayAutoSent(BroadcastTargets.Today(options.Value, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return await BirthdaySettings.ReadAsync(db, userId, options.Value, now, cancellationToken);
    }
}

/// <summary>
/// Starts each user's birthday wishes once a day, at the time they chose, with their chosen template. People who were
/// already wished today, who do not want broadcasts or who are not on WhatsApp are left out. The paced sender then sends
/// them one by one, birthday wishes before any other broadcast. If WhatsApp is not connected they wait and go out as soon
/// as it is connected again (the apps ask the user to reconnect).
/// </summary>
public sealed class BirthdayAutoSender(IAppDbContext db, IOptions<BroadcastOptions> options, TimeProvider clock, ILogger<BirthdayAutoSender> logger)
{
    /// <summary>Returns how many users' wishes were started.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var today = BroadcastTargets.Today(o, now);
        var due = await db.UserPreferences
            .Where(p => p.BirthdayAutoSend && p.BirthdaySendTime != null && p.BirthdayTemplateId != null &&
                (p.BirthdayAutoSentOn == null || p.BirthdayAutoSentOn < today))
            .ToListAsync(cancellationToken);

        var started = 0;
        foreach (var p in due.Where(p => BirthdaySettings.TimePassedToday(p.BirthdaySendTime!.Value, o, now)))
        {
            p.MarkBirthdayAutoSent(today);
            var template = await db.MessageTemplates.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == p.BirthdayTemplateId && t.UserId == p.UserId, cancellationToken);
            if (template is null)
            {
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var people = await BirthdayPeople.TodayAsync(db, p.UserId, today, cancellationToken);
            var wished = await BirthdayPeople.WishedTodayAsync(db, p.UserId, today, cancellationToken);
            var ids = people.Where(c => c.OptedOutAt is null && !wished.Contains(c.Id)).Select(c => c.Id).ToList();
            if (ids.Count == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var name = $"Birthday wishes · {today.ToString("d MMM", CultureInfo.InvariantCulture)}";
            var input = new BroadcastInput(name, null, null, null, null, null, ids, template.Text, ForBirthday: true);
            var created = await BroadcastCreation.CreateAsync(db, p.UserId, input, o, now, cancellationToken);
            if (created.Error is { } error)
            {
                // Nobody could get it (for example everyone needs details): today is done, try again tomorrow.
                logger.LogInformation("Automatic birthday wishes for {UserId} not started: {Code}", p.UserId, error.Code);
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            started++;
        }

        return started;
    }
}
