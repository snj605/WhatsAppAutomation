using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Roles;
using WhatsAppAutomation.Application.Features.Roles.Commands;
using WhatsAppAutomation.Application.Features.Roles.Queries;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class RolesController(ISender mediator) : ControllerBase
{
    /// <summary>Every permission that exists, grouped for the role editor.</summary>
    [HttpGet("permissions")]
    [HasPermission(Permissions.Roles.View)]
    [ProducesResponseType<IReadOnlyList<PermissionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissions(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPermissionsQuery(), cancellationToken);
        return this.ToActionResult(result, list => Ok(list.Select(p => new PermissionResponse(p.Name, p.Group, p.Description))));
    }

    [HttpGet("roles")]
    [HasPermission(Permissions.Roles.View)]
    [ProducesResponseType<IReadOnlyList<RoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetRolesQuery(), cancellationToken);
        return this.ToActionResult(result, roles => Ok(roles.Select(ToResponse)));
    }

    [HttpGet("roles/{id:guid}")]
    [HasPermission(Permissions.Roles.View)]
    [ProducesResponseType<RoleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRole(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetRoleQuery(id), cancellationToken);
        return this.ToActionResult(result, role => Ok(ToResponse(role)));
    }

    [HttpPost("roles")]
    [HasPermission(Permissions.Roles.Manage)]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRole(SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateRoleCommand(request.Name, request.Description, request.Permissions ?? []), cancellationToken);
        return this.ToActionResult(result, id => CreatedAtAction(nameof(GetRole), new { id }, new CreatedResponse(id)));
    }

    [HttpPut("roles/{id:guid}")]
    [HasPermission(Permissions.Roles.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateRole(Guid id, SaveRoleRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(
            new UpdateRoleCommand(id, request.Name, request.Description, request.Permissions ?? []), cancellationToken));

    [HttpDelete("roles/{id:guid}")]
    [HasPermission(Permissions.Roles.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteRoleCommand(id), cancellationToken));

    private static RoleResponse ToResponse(RoleDetails role) =>
        new(role.Id, role.Name, role.Description, role.IsSystem, role.Permissions, role.UserCount);
}
