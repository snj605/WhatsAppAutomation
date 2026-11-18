using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Contacts.Queries;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Sending;

/// <summary>
/// How the user's number sends. The user chooses only how many messages a day and the hours; the rhythm inside
/// them (pauses, how many in one go, rests) is the system's own and changes all the time (see <see cref="HumanPace"/>).
/// </summary>
/// <param name="DailyLimit">Messages a day, occasions and broadcasts together: 300 to 500, from the first day.</param>
/// <param name="SendFrom">"HH:mm": messages go out from this time…</param>
/// <param name="SendUntil">"HH:mm": …until this time. The same as SendFrom means any time.</param>
/// <param name="FitsPerDay">How many messages fit in a day with these hours (at most the daily limit).</param>
/// <param name="AverageSeconds">About how long one message takes on average, pauses and rests included.</param>
public sealed record SendingSettingsDto(
    int DailyLimit,
    string SendFrom,
    string SendUntil,
    string TimeZone,
    int FitsPerDay,
    int AverageSeconds);

/// <summary>The values the sender uses for one user.</summary>
internal sealed record UserSending(int DailyLimit, TimeOnly SendFrom, TimeOnly SendUntil)
{
    public static readonly UserSending Default = new(UserPreferences.MinDailyLimit, new TimeOnly(9, 0), new TimeOnly(21, 0));

    /// <summary>Set only by tests (Broadcasts:NoWaits): no pauses at all.</summary>
    public bool NoWaits { get; init; }

    public bool AnyTime => SendFrom == SendUntil;

    public static async Task<UserSending> ForAsync(IAppDbContext db, Guid userId, CancellationToken cancellationToken) =>
        await db.UserPreferences.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new UserSending(p.DailyLimit, p.SendFrom, p.SendUntil))
            .SingleOrDefaultAsync(cancellationToken) ?? Default;

    /// <summary>Hours of sending in a day: the window, or 24 for any time.</summary>
    public double WindowHours => AnyTime ? 24
        : SendFrom < SendUntil ? (SendUntil - SendFrom).TotalHours : 24 - (SendFrom - SendUntil).TotalHours;

    /// <summary>How many messages fit in one day's sending hours (at most the daily limit).</summary>
    public int FitsPerDay => HumanPace.FitsPerDay(DailyLimit, WindowHours);

    /// <summary>The average time one message takes, pauses and rests included.</summary>
    public double AverageSeconds => NoWaits ? 0 : HumanPace.AverageSeconds(DailyLimit, WindowHours);

    public SendingSettingsDto ToDto(BroadcastOptions options) => new(
        DailyLimit,
        SendFrom.ToString("HH:mm", CultureInfo.InvariantCulture),
        SendUntil.ToString("HH:mm", CultureInfo.InvariantCulture),
        options.TimeZone,
        FitsPerDay,
        (int)Math.Round(AverageSeconds));
}

public sealed record GetSendingSettingsQuery : IQuery<SendingSettingsDto>;

internal sealed class GetSendingSettingsQueryHandler(IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options)
    : IQueryHandler<GetSendingSettingsQuery, SendingSettingsDto>
{
    public async Task<Result<SendingSettingsDto>> Handle(GetSendingSettingsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        return (await UserSending.ForAsync(db, userId, cancellationToken)).ToDto(options.Value);
    }
}

public sealed record SetSendingSettingsCommand(int DailyLimit, string SendFrom, string SendUntil) : ICommand<SendingSettingsDto>;

internal sealed class SetSendingSettingsCommandValidator : AbstractValidator<SetSendingSettingsCommand>
{
    public SetSendingSettingsCommandValidator()
    {
        RuleFor(c => c.DailyLimit).InclusiveBetween(UserPreferences.MinDailyLimit, UserPreferences.MaxDailyLimit)
            .WithMessage($"Messages a day: choose {UserPreferences.MinDailyLimit} to {UserPreferences.MaxDailyLimit}.");
        RuleFor(c => c.SendFrom).Must(t => Modules.ModuleReads.TryParseTime(t, out _)).WithMessage("Choose the start time, like 09:00.");
        RuleFor(c => c.SendUntil).Must(t => Modules.ModuleReads.TryParseTime(t, out _)).WithMessage("Choose the end time, like 21:00.");
    }
}

internal sealed class SetSendingSettingsCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOptions<BroadcastOptions> options)
    : ICommandHandler<SetSendingSettingsCommand, SendingSettingsDto>
{
    public async Task<Result<SendingSettingsDto>> Handle(SetSendingSettingsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return BroadcastErrors.NotSignedIn;
        }

        Modules.ModuleReads.TryParseTime(command.SendFrom, out var from);
        Modules.ModuleReads.TryParseTime(command.SendUntil, out var until);
        var preferences = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (preferences is null)
        {
            preferences = new UserPreferences(userId, await Preferences.DefaultLanguageAsync(db, userId, cancellationToken));
            db.UserPreferences.Add(preferences);
        }

        preferences.SetSending(command.DailyLimit, from, until);
        await db.SaveChangesAsync(cancellationToken);
        return (await UserSending.ForAsync(db, userId, cancellationToken)).ToDto(options.Value);
    }
}
