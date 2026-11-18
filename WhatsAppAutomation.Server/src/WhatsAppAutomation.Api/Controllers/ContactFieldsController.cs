using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.ContactFields;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>
/// The signed-in user's own contact fields (City, Village, Relation, …): text, number, date or a choice. Values are
/// saved with each contact (contacts' "fields"), used in messages as {{key}}, imported from Excel and searchable.
/// Making and changing fields is part of "Create own modules", like occasions.
/// </summary>
[ApiController]
[Route("api/v1/contact-fields")]
[Produces("application/json")]
public sealed class ContactFieldsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Contacts.View)]
    [ProducesResponseType<IReadOnlyList<ContactFieldDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetContactFieldsQuery(), cancellationToken), Ok);

    [HttpPost]
    [HasPermission(Permissions.Modules.Manage)]
    [ProducesResponseType<ContactFieldDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateContactFieldRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new CreateContactFieldCommand(request.Name, request.Type, request.Options), cancellationToken), Ok);

    /// <summary>Renames a field or changes its choices; the type stays.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Modules.Manage)]
    [ProducesResponseType<ContactFieldDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, UpdateContactFieldRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new UpdateContactFieldCommand(id, request.Name, request.Options), cancellationToken), Ok);

    /// <summary>Deletes the field and its value on every contact.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Modules.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteContactFieldCommand(id), cancellationToken));
}
