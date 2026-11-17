using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Roles.Queries;

public sealed record GetRoleQuery(Guid RoleId) : IQuery<RoleDetails>;

internal sealed class GetRoleQueryHandler(IRoleService roles) : IQueryHandler<GetRoleQuery, RoleDetails>
{
    public async Task<Result<RoleDetails>> Handle(GetRoleQuery query, CancellationToken cancellationToken) =>
        await roles.FindAsync(query.RoleId, cancellationToken) is { } role ? role : RoleErrors.NotFound;
}
