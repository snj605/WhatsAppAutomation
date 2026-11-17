using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Features.Members;
using WhatsAppAutomation.Application.Features.Members.Commands;
using WhatsAppAutomation.Application.Features.Tenants.Commands;
using WhatsAppAutomation.Application.Features.Tenants.Queries;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>
/// The platform admin manages tenants and their admins. It sees counts only: never a tenant's contacts,
/// templates or messages.
/// </summary>
[ApiController]
[Route("api/v1/platform/tenants")]
[Produces("application/json")]
[HasPermission(Permissions.Platform.Tenants)]
public sealed class TenantsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TenantResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenants(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTenantsQuery(), cancellationToken);
        return this.ToActionResult(result, tenants => Ok(tenants.Select(t =>
            new TenantResponse(t.Id, t.Name, t.IsActive, t.CreatedAt, t.Users, t.Admins))));
    }

    /// <summary>Creates a tenant with its first admin.</summary>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTenant(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateTenantCommand(request.Name, request.AdminUserName, request.AdminDisplayName, request.AdminPassword),
            cancellationToken);
        return this.ToActionResult(result, id => Created($"/api/v1/platform/tenants/{id}", new CreatedResponse(id)));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RenameTenant(Guid id, RenameTenantRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new RenameTenantCommand(id, request.Name), cancellationToken));

    [HttpPut("{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(Guid id, SetActiveRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SetTenantActiveCommand(id, request.IsActive), cancellationToken));

    /// <summary>Deletes a turned-off tenant with all its people and their data. Cannot be undone.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteTenant(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteTenantCommand(id), cancellationToken));

    [HttpGet("{id:guid}/admins")]
    [ProducesResponseType<IReadOnlyList<TenantAdminResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAdmins(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTenantAdminsQuery(id), cancellationToken);
        return this.ToActionResult(result, admins => Ok(admins.Select(a =>
            new TenantAdminResponse(a.Id, a.UserName, a.DisplayName, a.IsActive, a.CreatedAt))));
    }

    [HttpPost("{id:guid}/admins")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAdmin(Guid id, CreateTenantAdminRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateMemberCommand(request.UserName, request.DisplayName, request.Password, IsAdmin: true, [], TenantId: id),
            cancellationToken);
        return this.ToActionResult(result, adminId => Created($"/api/v1/platform/tenants/{id}/admins/{adminId}", new CreatedResponse(adminId)));
    }

    [HttpPut("{id:guid}/admins/{adminId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateAdmin(Guid id, Guid adminId, UpdateTenantAdminRequest request, CancellationToken cancellationToken) =>
        SendForAdmin(id, adminId, new UpdateMemberCommand(adminId, request.DisplayName, IsAdmin: true, []), cancellationToken);

    [HttpPut("{id:guid}/admins/{adminId:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> SetAdminActive(Guid id, Guid adminId, SetActiveRequest request, CancellationToken cancellationToken) =>
        SendForAdmin(id, adminId, new SetMemberActiveCommand(adminId, request.IsActive), cancellationToken);

    [HttpPut("{id:guid}/admins/{adminId:guid}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ResetAdminPassword(Guid id, Guid adminId, SetPasswordRequest request, CancellationToken cancellationToken) =>
        SendForAdmin(id, adminId, new ResetMemberPasswordCommand(adminId, request.Password), cancellationToken);

    [HttpDelete("{id:guid}/admins/{adminId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> DeleteAdmin(Guid id, Guid adminId, CancellationToken cancellationToken) =>
        SendForAdmin(id, adminId, new DeleteMemberCommand(adminId), cancellationToken);

    /// <summary>Runs the command only when the admin belongs to the tenant in the address.</summary>
    private async Task<IActionResult> SendForAdmin(Guid tenantId, Guid adminId, ICommand command, CancellationToken cancellationToken)
    {
        var admins = await mediator.Send(new GetTenantAdminsQuery(tenantId), cancellationToken);
        if (!admins.IsSuccess)
        {
            return this.ToProblem(admins.Error!);
        }

        return admins.Value.Any(a => a.Id == adminId)
            ? this.ToActionResult(await mediator.Send(command, cancellationToken))
            : this.ToProblem(MemberErrors.NotFound);
    }
}
