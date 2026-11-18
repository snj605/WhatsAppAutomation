using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Platform;

/// <summary>One tenant on the dashboard: counts only.</summary>
public sealed record DashboardTenantDto(
    Guid Id, string Name, bool IsActive, int Users, int NumbersConnected, int Numbers, int SentToday, int FailedToday, IReadOnlyList<string> Modules);

/// <summary>A number that needs a look. Only the last digits are shown.</summary>
/// <param name="Status">"disconnected", "logged_out", "restricted", "reconnecting", …</param>
public sealed record NumberAlertDto(string TenantName, string UserName, string? NumberEnd, string Status, DateTimeOffset? Since);

public sealed record DashboardDto(
    int Tenants,
    int ActiveTenants,
    int Users,
    int NumbersConnected,
    int Numbers,
    int MessagesToday,
    int FailedToday,
    IReadOnlyList<DashboardTenantDto> TenantRows,
    IReadOnlyList<NumberAlertDto> Alerts,
    IReadOnlyList<AuditEntryDto> RecentActivity);

/// <summary>The platform admin's dashboard. Counts only: never contacts, templates or message text.</summary>
public sealed record GetDashboardQuery : IQuery<DashboardDto>;

internal sealed class GetDashboardQueryHandler(
    IAppDbContext db, IIdentityService identity, IOptions<BroadcastOptions> options, TimeProvider clock)
    : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    public async Task<Result<DashboardDto>> Handle(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var startOfDay = new PacingRules(options.Value).StartOfToday(now);
        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var people = await identity.UsersByTenantAsync(cancellationToken);
        var tenantOf = people.ToDictionary(p => p.UserId, p => p.TenantId);

        var accounts = await db.WhatsAppAccounts.AsNoTracking()
            .Select(a => new { a.Id, a.UserId, a.Status, a.PhoneNumber, a.StatusChangedAt })
            .ToListAsync(cancellationToken);
        var today = await db.OutgoingMessages.AsNoTracking()
            .Where(m => m.CreatedAt >= startOfDay)
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Sent = g.Count(m => m.Status != OutgoingMessageStatus.Failed), Failed = g.Count(m => m.Status == OutgoingMessageStatus.Failed) })
            .ToListAsync(cancellationToken);
        var modules = await db.OccasionModules.AsNoTracking().Select(m => new { m.UserId, m.Name }).ToListAsync(cancellationToken);

        var rows = tenants.Select(t =>
        {
            var users = people.Where(p => p.TenantId == t.Id).Select(p => p.UserId).ToHashSet();
            var numbers = accounts.Where(a => users.Contains(a.UserId) && a.Status != WhatsAppConnectionStatus.NotLinked).ToList();
            var sent = today.Where(x => users.Contains(x.UserId)).ToList();
            return new DashboardTenantDto(
                t.Id, t.Name, t.IsActive, users.Count,
                numbers.Count(a => a.Status == WhatsAppConnectionStatus.Connected), numbers.Count,
                sent.Sum(x => x.Sent), sent.Sum(x => x.Failed),
                [.. modules.Where(m => users.Contains(m.UserId)).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order()]);
        }).ToList();

        var names = people.ToDictionary(p => p.UserId, p => p.DisplayName);
        var tenantNames = tenants.ToDictionary(t => t.Id, t => t.Name);
        var alerts = accounts
            .Where(a => a.Status is WhatsAppConnectionStatus.Disconnected or WhatsAppConnectionStatus.LoggedOut
                or WhatsAppConnectionStatus.Restricted or WhatsAppConnectionStatus.Reconnecting)
            .Where(a => tenantOf.ContainsKey(a.UserId))
            .Select(a => new NumberAlertDto(
                tenantOf[a.UserId] is { } tid ? tenantNames.GetValueOrDefault(tid, "") : "",
                names.GetValueOrDefault(a.UserId, ""),
                a.PhoneNumber is { Length: > 4 } p ? p[^4..] : null,
                System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(a.Status.ToString()),
                a.StatusChangedAt))
            .OrderByDescending(a => a.Since)
            .Take(20)
            .ToList();

        var audit = await new GetAuditQueryHandler(db).Handle(new GetAuditQuery(1, 10), cancellationToken);
        return new DashboardDto(
            tenants.Count,
            tenants.Count(t => t.IsActive),
            people.Count,
            rows.Sum(r => r.NumbersConnected),
            rows.Sum(r => r.Numbers),
            today.Sum(x => x.Sent),
            today.Sum(x => x.Failed),
            rows,
            alerts,
            audit.IsSuccess ? audit.Value.Items : []);
    }
}
