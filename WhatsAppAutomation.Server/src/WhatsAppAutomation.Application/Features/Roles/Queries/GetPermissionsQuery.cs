using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Roles.Queries;

/// <summary>Every permission that exists, for the role editor's checklist.</summary>
public sealed record GetPermissionsQuery : IQuery<IReadOnlyList<PermissionDefinition>>;

internal sealed class GetPermissionsQueryHandler : IQueryHandler<GetPermissionsQuery, IReadOnlyList<PermissionDefinition>>
{
    public Task<Result<IReadOnlyList<PermissionDefinition>>> Handle(GetPermissionsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<PermissionDefinition>>.Success(Permissions.All));
}
