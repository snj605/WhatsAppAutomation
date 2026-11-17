using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Roles.Queries;

public sealed record GetRolesQuery : IQuery<IReadOnlyList<RoleDetails>>;

internal sealed class GetRolesQueryHandler(IRoleService roles) : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDetails>>
{
    public async Task<Result<IReadOnlyList<RoleDetails>>> Handle(GetRolesQuery query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<RoleDetails>>.Success(await roles.ListAsync(cancellationToken));
}
