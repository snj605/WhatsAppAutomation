using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.Members.Commands;
using WhatsAppAutomation.Application.Features.Members.Queries;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>A tenant admin manages the people of their own tenant. Nobody manages their own account here.</summary>
[ApiController]
[Route("api/v1/members")]
[Produces("application/json")]
[HasPermission(Permissions.Members.Manage)]
public sealed class MembersController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MemberResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMembers(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetMembersQuery(), cancellationToken);
        return this.ToActionResult(result, members => Ok(members.Select(ToResponse)));
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMember(CreateMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateMemberCommand(request.UserName, request.DisplayName, request.Password, request.IsAdmin, request.Switches ?? []),
            cancellationToken);
        return this.ToActionResult(result, id => Created($"/api/v1/members/{id}", new CreatedResponse(id)));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMember(Guid id, UpdateMemberRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(
            new UpdateMemberCommand(id, request.DisplayName, request.IsAdmin, request.Switches ?? []), cancellationToken));

    [HttpPut("{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetActive(Guid id, SetActiveRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SetMemberActiveCommand(id, request.IsActive), cancellationToken));

    /// <summary>Sets a new password for someone who forgot theirs; they choose their own at the next sign-in.</summary>
    [HttpPut("{id:guid}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid id, SetPasswordRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new ResetMemberPasswordCommand(id, request.Password), cancellationToken));

    /// <summary>Deletes the person and everything that is theirs. Cannot be undone.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteMember(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteMemberCommand(id), cancellationToken));

    private static MemberResponse ToResponse(MemberDto member) => new(
        member.User.Id,
        member.User.UserName,
        member.User.DisplayName,
        UserKinds.ToCode(member.User.Kind),
        member.User.IsActive,
        member.User.Switches,
        member.WhatsAppStatus,
        member.WhatsAppNumber,
        member.User.CreatedAt);
}
