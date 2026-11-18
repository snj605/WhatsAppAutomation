using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Platform;

/// <summary>Writes the audit log: who did what. Never message text or contacts' numbers.</summary>
public sealed class AuditLog(IAppDbContext db, ICurrentUser currentUser, IIdentityService identity, TimeProvider clock)
{
    public async Task WriteAsync(string action, string text, Guid? tenantId, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId is { } id ? await identity.FindUserAsync(id, cancellationToken) : null;
        db.AuditEntries.Add(new AuditEntry(actor?.Id, actor?.DisplayName ?? "System", tenantId ?? actor?.TenantId, action, text, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record AuditEntryDto(DateTimeOffset At, string ActorName, string? TenantName, string Action, string Text);

public sealed record AuditPage(IReadOnlyList<AuditEntryDto> Items, int Total);

/// <summary>The audit log, newest first, a page at a time (platform admin).</summary>
public sealed record GetAuditQuery(int Page = 1, int PageSize = 50) : IQuery<AuditPage>;

internal sealed class GetAuditQueryHandler(IAppDbContext db) : IQueryHandler<GetAuditQuery, AuditPage>
{
    public async Task<Result<AuditPage>> Handle(GetAuditQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var total = await db.AuditEntries.CountAsync(cancellationToken);
        var rows = await (
            from a in db.AuditEntries.AsNoTracking()
            orderby a.At descending
            select new { a, Tenant = db.Tenants.Where(t => t.Id == a.TenantId).Select(t => t.Name).FirstOrDefault() })
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(cancellationToken);
        return new AuditPage([.. rows.Select(r => new AuditEntryDto(r.a.At, r.a.ActorName, r.Tenant, r.a.Action, r.a.Text))], total);
    }
}
