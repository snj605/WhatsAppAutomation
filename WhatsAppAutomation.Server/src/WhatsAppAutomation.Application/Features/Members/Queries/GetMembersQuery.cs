using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Members.Queries;

/// <summary>A person of the tenant, with the state of their WhatsApp link so the admin sees who still has to link.</summary>
public sealed record MemberDto(UserDetails User, WhatsAppConnectionStatus WhatsAppStatus, string? WhatsAppNumber);

/// <summary>The people of the caller's own tenant, admins included.</summary>
public sealed record GetMembersQuery : IQuery<IReadOnlyList<MemberDto>>;

internal sealed class GetMembersQueryHandler(MemberAccess access, IIdentityService identity, IAppDbContext db)
    : IQueryHandler<GetMembersQuery, IReadOnlyList<MemberDto>>
{
    public async Task<Result<IReadOnlyList<MemberDto>>> Handle(GetMembersQuery query, CancellationToken cancellationToken)
    {
        if (await access.CallerAsync(cancellationToken) is not { Kind: UserKind.TenantAdmin, TenantId: { } tenantId })
        {
            return MemberErrors.NotFound;
        }

        var users = await identity.ListUsersAsync(tenantId, cancellationToken);
        var ids = users.Select(u => u.Id).ToList();
        var accounts = await db.WhatsAppAccounts.AsNoTracking()
            .Where(a => ids.Contains(a.UserId))
            .Select(a => new { a.UserId, a.Status, a.PhoneNumber })
            .ToDictionaryAsync(a => a.UserId, cancellationToken);

        return Result<IReadOnlyList<MemberDto>>.Success([.. users.Select(u => accounts.TryGetValue(u.Id, out var a)
            ? new MemberDto(u, a.Status, a.PhoneNumber)
            : new MemberDto(u, WhatsAppConnectionStatus.NotLinked, null))]);
    }
}
