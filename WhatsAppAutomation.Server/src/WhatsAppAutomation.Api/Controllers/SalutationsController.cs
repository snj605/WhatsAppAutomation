using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.Salutations;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>
/// The signed-in user's salutations (master data), each in English and Gujarati, for example "Shri / શ્રી". A contact
/// picks one; {{salutation}} becomes the text in the contact's language. Changing the list is part of "Create own
/// modules", like occasions and contact fields.
/// </summary>
[ApiController]
[Route("api/v1/salutations")]
[Produces("application/json")]
public sealed class SalutationsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Contacts.View)]
    [ProducesResponseType<IReadOnlyList<SalutationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetSalutationsQuery(), cancellationToken), Ok);

    [HttpPost]
    [HasPermission(Permissions.Modules.Manage)]
    [ProducesResponseType<SalutationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(SaveSalutationRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveSalutationCommand(null, request.NameEn, request.NameGu), cancellationToken), Ok);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Modules.Manage)]
    [ProducesResponseType<SalutationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, SaveSalutationRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveSalutationCommand(id, request.NameEn, request.NameGu), cancellationToken), Ok);

    /// <summary>Deletes it; contacts that had it are left without a salutation.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Modules.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteSalutationCommand(id), cancellationToken));
}
