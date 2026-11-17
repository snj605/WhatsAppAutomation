using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Members.Queries;

/// <summary>The people of the caller's own tenant, admins included.</summary>
public sealed record GetMembersQuery : IQuery<IReadOnlyList<UserDetails>>;

internal sealed class GetMembersQueryHandler(MemberAccess access, IIdentityService identity)
    : IQueryHandler<GetMembersQuery, IReadOnlyList<UserDetails>>
{
    public async Task<Result<IReadOnlyList<UserDetails>>> Handle(GetMembersQuery query, CancellationToken cancellationToken)
    {
        if (await access.CallerAsync(cancellationToken) is not { Kind: UserKind.TenantAdmin, TenantId: { } tenantId })
        {
            return MemberErrors.NotFound;
        }

        return Result<IReadOnlyList<UserDetails>>.Success(await identity.ListUsersAsync(tenantId, cancellationToken));
    }
}
