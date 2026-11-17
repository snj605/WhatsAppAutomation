using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Tenants.Queries;

/// <summary>A tenant as the platform admin sees it: counts only, nothing of what is inside.</summary>
public sealed record TenantSummary(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt, int Users, int Admins);

public sealed record GetTenantsQuery : IQuery<IReadOnlyList<TenantSummary>>;

internal sealed class GetTenantsQueryHandler(IAppDbContext db, IIdentityService identity)
    : IQueryHandler<GetTenantsQuery, IReadOnlyList<TenantSummary>>
{
    public async Task<Result<IReadOnlyList<TenantSummary>>> Handle(GetTenantsQuery query, CancellationToken cancellationToken)
    {
        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var counts = await identity.CountByTenantAsync(cancellationToken);
        return Result<IReadOnlyList<TenantSummary>>.Success([.. tenants.Select(t =>
        {
            var (users, admins) = counts.GetValueOrDefault(t.Id);
            return new TenantSummary(t.Id, t.Name, t.IsActive, t.CreatedAt, users, admins);
        })]);
    }
}

/// <summary>The admins of one tenant, for the platform admin.</summary>
public sealed record GetTenantAdminsQuery(Guid TenantId) : IQuery<IReadOnlyList<UserDetails>>;

internal sealed class GetTenantAdminsQueryHandler(IAppDbContext db, IIdentityService identity)
    : IQueryHandler<GetTenantAdminsQuery, IReadOnlyList<UserDetails>>
{
    public async Task<Result<IReadOnlyList<UserDetails>>> Handle(GetTenantAdminsQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == query.TenantId, cancellationToken))
        {
            return TenantErrors.NotFound;
        }

        var users = await identity.ListUsersAsync(query.TenantId, cancellationToken);
        return Result<IReadOnlyList<UserDetails>>.Success([.. users.Where(u => u.Kind == UserKind.TenantAdmin)]);
    }
}
