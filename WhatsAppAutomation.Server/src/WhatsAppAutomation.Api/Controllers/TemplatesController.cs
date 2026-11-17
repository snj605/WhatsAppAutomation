using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.Templates;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>The signed-in user's own message templates.</summary>
[ApiController]
[Route("api/v1/templates")]
[Produces("application/json")]
public sealed class TemplatesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Broadcasts.Send)]
    [ProducesResponseType<IReadOnlyList<TemplateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetTemplatesQuery(), cancellationToken), Ok);

    [HttpPost]
    [HasPermission(Permissions.Broadcasts.Send)]
    [ProducesResponseType<TemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(SaveTemplateRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveTemplateCommand(null, request.Name, request.Text), cancellationToken), Ok);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Broadcasts.Send)]
    [ProducesResponseType<TemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, SaveTemplateRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveTemplateCommand(id, request.Name, request.Text), cancellationToken), Ok);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Broadcasts.Send)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteTemplateCommand(id), cancellationToken));
}
