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

/// <summary>How the user's number sends: chosen by the user.</summary>
/// <param name="DailyLimit">Messages a day, occasions and broadcasts together: 300 to 500, from the first day.</param>
/// <param name="ChunkSize">Messages sent in one go before a rest.</param>
/// <param name="ChunkRestMinutes">Rest between chunks.</param>
/// <param name="SendFrom">"HH:mm": messages go out from this time…</param>
/// <param name="SendUntil">"HH:mm": …until this time. The same as SendFrom means any time.</param>
/// <param name="MinGapSeconds">The pause between two messages is picked at random from this…</param>
/// <param name="MaxGapSeconds">…to this, so it looks human.</param>
/// <param name="FitsPerDay">How many messages fit in a day with these hours, gaps and rests (at most the daily limit).</param>
public sealed record SendingSettingsDto(
    int DailyLimit,
    int ChunkSize,
    int ChunkRestMinutes,
    string SendFrom,
    string SendUntil,
    string TimeZone,
    int MinGapSeconds,
    int MaxGapSeconds,
    int FitsPerDay);

/// <summary>The values the sender uses for one user.</summary>
internal sealed record UserSending(
    int DailyLimit, int ChunkSize, int ChunkRestMinutes, TimeOnly SendFrom, TimeOnly SendUntil, int MinGapSeconds, int MaxGapSeconds)
{
    public static readonly UserSending Default = new(UserPreferences.MinDailyLimit, 50, 15, new TimeOnly(9, 0), new TimeOnly(21, 0), 30, 90);

    public bool AnyTime => SendFrom == SendUntil;

    public static async Task<UserSending> ForAsync(IAppDbContext db, Guid userId, CancellationToken cancellationToken) =>
        await db.UserPreferences.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new UserSending(p.DailyLimit, p.ChunkSize, p.ChunkRestMinutes, p.SendFrom, p.SendUntil, p.MinGapSeconds, p.MaxGapSeconds))
            .SingleOrDefaultAsync(cancellationToken) ?? Default;

    /// <summary>Hours of sending in a day: the window, or 24 for any time.</summary>
    public double WindowHours => AnyTime ? 24
        : SendFrom < SendUntil ? (SendUntil - SendFrom).TotalHours : 24 - (SendFrom - SendUntil).TotalHours;

    /// <summary>How many messages fit in one day's sending hours with these gaps and rests (at most the daily limit).</summary>
    public int FitsPerDay
    {
        get
        {
            var perMessage = (MinGapSeconds + MaxGapSeconds) / 2.0;
            var perChunk = ChunkSize * perMessage + ChunkRestMinutes * 60.0;
            var seconds = WindowHours * 3600;
            var full = Math.Floor(seconds / perChunk);
            var rest = seconds - full * perChunk;
            var count = (int)(full * ChunkSize + Math.Min(ChunkSize, Math.Floor(rest / perMessage)));
            return Math.Min(DailyLimit, Math.Max(0, count));
        }
    }

    public SendingSettingsDto ToDto(BroadcastOptions options) => new(
        DailyLimit,
        ChunkSize,
        ChunkRestMinutes,
        SendFrom.ToString("HH:mm", CultureInfo.InvariantCulture),
        SendUntil.ToString("HH:mm", CultureInfo.InvariantCulture),
        options.TimeZone,
        MinGapSeconds,
        MaxGapSeconds,
        FitsPerDay);
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

public sealed record SetSendingSettingsCommand(
    int DailyLimit, int ChunkSize, int ChunkRestMinutes, string SendFrom, string SendUntil, int MinGapSeconds, int MaxGapSeconds)
    : ICommand<SendingSettingsDto>;

internal sealed class SetSendingSettingsCommandValidator : AbstractValidator<SetSendingSettingsCommand>
{
    public SetSendingSettingsCommandValidator()
    {
        RuleFor(c => c.DailyLimit).InclusiveBetween(UserPreferences.MinDailyLimit, UserPreferences.MaxDailyLimit)
            .WithMessage($"Messages a day: choose {UserPreferences.MinDailyLimit} to {UserPreferences.MaxDailyLimit}.");
        RuleFor(c => c.ChunkSize).InclusiveBetween(1, UserPreferences.MaxChunkSize)
            .WithMessage($"Messages in one go: choose 1 to {UserPreferences.MaxChunkSize}.");
        RuleFor(c => c.ChunkRestMinutes).InclusiveBetween(0, UserPreferences.MaxChunkRestMinutes)
            .WithMessage($"Rest between chunks: choose 0 to {UserPreferences.MaxChunkRestMinutes} minutes.");
        RuleFor(c => c.SendFrom).Must(t => Modules.ModuleReads.TryParseTime(t, out _)).WithMessage("Choose the start time, like 09:00.");
        RuleFor(c => c.SendUntil).Must(t => Modules.ModuleReads.TryParseTime(t, out _)).WithMessage("Choose the end time, like 21:00.");
        RuleFor(c => c.MinGapSeconds).InclusiveBetween(UserPreferences.MinGap, UserPreferences.MaxGap)
            .WithMessage($"Shortest pause: choose {UserPreferences.MinGap} to {UserPreferences.MaxGap} seconds.");
        RuleFor(c => c.MaxGapSeconds).InclusiveBetween(UserPreferences.MinGap, UserPreferences.MaxGap)
            .GreaterThanOrEqualTo(c => c.MinGapSeconds).WithMessage("The longest pause must not be shorter than the shortest one.");
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

        preferences.SetSending(
            command.DailyLimit, command.ChunkSize, command.ChunkRestMinutes, from, until, command.MinGapSeconds, command.MaxGapSeconds);
        await db.SaveChangesAsync(cancellationToken);
        return (await UserSending.ForAsync(db, userId, cancellationToken)).ToDto(options.Value);
    }
}
