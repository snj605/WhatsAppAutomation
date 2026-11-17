using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts;

/// <summary>Converts between the WhatsApp check enum and the codes the API uses.</summary>
public static class WhatsAppChecks
{
    public const string UncheckedCode = "unchecked";
    public const string PendingCode = "pending";
    public const string OnWhatsAppCode = "on_whatsapp";
    public const string NotOnWhatsAppCode = "not_on_whatsapp";

    /// <summary>Contacts list filter: everyone, including numbers without WhatsApp.</summary>
    public const string AllFilter = "all";

    public static string ToCode(WhatsAppCheck check) => check switch
    {
        WhatsAppCheck.Pending => PendingCode,
        WhatsAppCheck.OnWhatsApp => OnWhatsAppCode,
        WhatsAppCheck.NotOnWhatsApp => NotOnWhatsAppCode,
        _ => UncheckedCode,
    };

    public static WhatsAppCheck Parse(string value) => value switch
    {
        PendingCode => WhatsAppCheck.Pending,
        OnWhatsAppCode => WhatsAppCheck.OnWhatsApp,
        NotOnWhatsAppCode => WhatsAppCheck.NotOnWhatsApp,
        _ => WhatsAppCheck.Unchecked,
    };

    /// <summary>Accepted values of the contacts list "whatsApp" filter.</summary>
    public static readonly IReadOnlyList<string> Filters = [AllFilter, UncheckedCode, OnWhatsAppCode, NotOnWhatsAppCode];

    public const string NotOnWhatsAppReason = "This number is not on WhatsApp.";
}

/// <param name="Unchecked">Never checked (saved before the check existed). Checked only when the user asks.</param>
/// <param name="Pending">Waiting for the check; done automatically while WhatsApp is connected.</param>
/// <param name="Connected">Whether the user's WhatsApp is connected, so pending numbers are being checked now.</param>
public sealed record WhatsAppCheckSummaryDto(int Unchecked, int Pending, int OnWhatsApp, int NotOnWhatsApp, bool Connected);

internal static class WhatsAppCheckSummary
{
    public static async Task<WhatsAppCheckSummaryDto> ForUserAsync(IAppDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var counts = await db.Contacts.AsNoTracking()
            .Where(c => c.UserId == userId)
            .GroupBy(c => c.WhatsAppCheck)
            .Select(g => new { Check = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var connected = await db.WhatsAppAccounts.AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Status == WhatsAppConnectionStatus.Connected, cancellationToken);
        int CountOf(WhatsAppCheck check) => counts.Where(x => x.Check == check).Sum(x => x.Count);
        return new WhatsAppCheckSummaryDto(
            CountOf(WhatsAppCheck.Unchecked),
            CountOf(WhatsAppCheck.Pending),
            CountOf(WhatsAppCheck.OnWhatsApp),
            CountOf(WhatsAppCheck.NotOnWhatsApp),
            connected);
    }
}

/// <summary>How many of the user's contacts are checked, waiting, on WhatsApp and not on WhatsApp.</summary>
public sealed record GetWhatsAppCheckSummaryQuery : IQuery<WhatsAppCheckSummaryDto>;

internal sealed class GetWhatsAppCheckSummaryQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetWhatsAppCheckSummaryQuery, WhatsAppCheckSummaryDto>
{
    public async Task<Result<WhatsAppCheckSummaryDto>> Handle(GetWhatsAppCheckSummaryQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        return await WhatsAppCheckSummary.ForUserAsync(db, userId, cancellationToken);
    }
}

/// <summary>
/// Queues numbers for the WhatsApp check. Without ids: every contact that was never checked. With ids: those
/// contacts, whatever their last result (for "Check again"). The check itself runs in the background.
/// </summary>
public sealed record RequestWhatsAppCheckCommand(IReadOnlyList<Guid>? ContactIds) : ICommand<WhatsAppCheckSummaryDto>;

internal sealed class RequestWhatsAppCheckCommandValidator : AbstractValidator<RequestWhatsAppCheckCommand>
{
    public RequestWhatsAppCheckCommandValidator()
    {
        RuleFor(c => c.ContactIds).Must(ids => ids is null || ids.Count <= 1000).WithMessage("Check at most 1000 contacts at once.");
    }
}

internal sealed class RequestWhatsAppCheckCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<RequestWhatsAppCheckCommand, WhatsAppCheckSummaryDto>
{
    public async Task<Result<WhatsAppCheckSummaryDto>> Handle(RequestWhatsAppCheckCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var ids = command.ContactIds?.Distinct().ToList() ?? [];
        var contacts = ids.Count == 0
            ? await db.Contacts.Where(c => c.UserId == userId && c.WhatsAppCheck == WhatsAppCheck.Unchecked).ToListAsync(cancellationToken)
            : await db.Contacts.Where(c => c.UserId == userId && ids.Contains(c.Id)).ToListAsync(cancellationToken);
        foreach (var contact in contacts)
        {
            contact.RequestWhatsAppCheck();
        }

        if (contacts.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return await WhatsAppCheckSummary.ForUserAsync(db, userId, cancellationToken);
    }
}

/// <summary>
/// Checks waiting numbers on WhatsApp, a small batch per connected number on each run, so a large import is
/// spread out instead of asking WhatsApp about thousands of numbers at once. Numbers without WhatsApp leave
/// their groups. Runs every few seconds in the background (WhatsAppNumberCheckService).
/// </summary>
public sealed class WhatsAppNumberChecker(
    IAppDbContext db,
    IWhatsAppGateway gateway,
    IIdentityService identity,
    IWhatsAppNotifier notifier,
    TimeProvider clock,
    ILogger<WhatsAppNumberChecker> logger)
{
    /// <summary>Numbers asked about per WhatsApp number per run.</summary>
    public const int BatchSize = 20;

    /// <summary>Returns how many contacts got a result in this run.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var accounts = await db.WhatsAppAccounts.AsNoTracking()
            .Where(a => a.Status == WhatsAppConnectionStatus.Connected &&
                db.Contacts.Any(c => c.UserId == a.UserId && c.WhatsAppCheck == WhatsAppCheck.Pending))
            .Select(a => new { a.Id, a.UserId })
            .ToListAsync(cancellationToken);

        var done = 0;
        foreach (var account in accounts)
        {
            if (!await identity.MaySendAsync(account.UserId, cancellationToken))
            {
                continue;
            }

            var contacts = await db.Contacts.Include(c => c.Groups)
                .Where(c => c.UserId == account.UserId && c.WhatsAppCheck == WhatsAppCheck.Pending)
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (contacts.Count == 0)
            {
                continue;
            }

            var numbers = contacts.Select(c => c.PhoneNumber).Distinct().ToList();
            var result = await gateway.CheckNumbersAsync(account.Id, numbers, cancellationToken);
            if (result.Error is { } error)
            {
                // Not connected any more or the gateway is down: they stay waiting and are tried on a later run.
                logger.LogWarning("WhatsApp number check postponed: {Code}", error.Code);
                continue;
            }

            var now = clock.GetUtcNow();
            foreach (var contact in contacts)
            {
                if (result.Value.TryGetValue(contact.PhoneNumber, out var onWhatsApp))
                {
                    contact.RecordWhatsAppCheck(onWhatsApp, now);
                    done++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await notifier.ContactsCheckedAsync(
                account.UserId, await WhatsAppCheckSummary.ForUserAsync(db, account.UserId, cancellationToken), cancellationToken);
        }

        return done;
    }
}
