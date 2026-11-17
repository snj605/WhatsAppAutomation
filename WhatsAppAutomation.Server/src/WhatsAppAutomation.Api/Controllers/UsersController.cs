using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Users;
using WhatsAppAutomation.Application.Features.Users.Commands;
using WhatsAppAutomation.Application.Features.Users.Queries;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public sealed class UsersController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    [ProducesResponseType<IReadOnlyList<UserDetailsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetUsersQuery(), cancellationToken);
        return this.ToActionResult(result, users => Ok(users.Select(ToResponse)));
    }

    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateUserCommand(request.UserName, request.DisplayName, request.Password, request.RoleIds ?? []), cancellationToken);
        return this.ToActionResult(result, id => Created($"/api/v1/users/{id}", new CreatedResponse(id)));
    }

    [HttpPut("{id:guid}/roles")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetRoles(Guid id, SetUserRolesRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SetUserRolesCommand(id, request.RoleIds ?? []), cancellationToken));

    [HttpPut("{id:guid}/active")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetActive(Guid id, SetUserActiveRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SetUserActiveCommand(id, request.IsActive), cancellationToken));

    /// <summary>Deletes the user and everything that is theirs. Cannot be undone.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteUserCommand(id), cancellationToken));

    private static UserDetailsResponse ToResponse(UserDetails user) => new(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.IsActive,
        [.. user.Roles.Select(r => new RoleRefResponse(r.Id, r.Name))],
        user.CreatedAt);
}
