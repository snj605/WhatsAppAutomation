using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Users.Queries;

public sealed record GetUsersQuery : IQuery<IReadOnlyList<UserDetails>>;

internal sealed class GetUsersQueryHandler(IIdentityService identity) : IQueryHandler<GetUsersQuery, IReadOnlyList<UserDetails>>
{
    public async Task<Result<IReadOnlyList<UserDetails>>> Handle(GetUsersQuery query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<UserDetails>>.Success(await identity.ListUsersAsync(cancellationToken));
}
