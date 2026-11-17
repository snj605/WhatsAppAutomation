using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.Modules;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>
/// The signed-in user's occasion modules (Birthday, Asthi visarjan, …): each with its people's dates, a template
/// and a time; today's wishes go out by themselves, before any broadcast. Manual sending of a day's wishes goes
/// through POST /api/v1/broadcasts with moduleId.
/// </summary>
[ApiController]
[Route("api/v1/modules")]
[Produces("application/json")]
[HasPermission(Permissions.Modules.Manage)]
public sealed class ModulesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ModuleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetModulesQuery(), cancellationToken), Ok);

    [HttpPost]
    [ProducesResponseType<ModuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateModuleRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new CreateModuleCommand(request.Name, request.Kind, request.ForNameLabel), cancellationToken), Ok);

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ModuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, UpdateModuleRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new UpdateModuleCommand(id, request.Name, request.ForNameLabel), cancellationToken), Ok);

    /// <summary>Deletes the module and its people's dates; waiting wishes are cancelled. Contacts stay.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteModuleCommand(id), cancellationToken));

    [HttpPut("{id:guid}/sending")]
    [ProducesResponseType<ModuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSending(Guid id, ModuleSendingRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(
            new SetModuleSendingCommand(id, request.Enabled, request.Time, request.TemplateId), cancellationToken), Ok);

    /// <summary>Today and the next days (default 7): who has the occasion, and today's status per person.</summary>
    [HttpGet("{id:guid}/days")]
    [ProducesResponseType<IReadOnlyList<ModuleDayPersonDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Days(Guid id, [FromQuery] int days = 7, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await mediator.Send(new GetModuleDaysQuery(id, days), cancellationToken), Ok);

    /// <summary>People with a date in a Yearly or Once module, by date, a page at a time.</summary>
    [HttpGet("{id:guid}/entries")]
    [ProducesResponseType<ModuleEntriesPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Entries(
        Guid id, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await mediator.Send(new GetModuleEntriesQuery(id, search, page, pageSize), cancellationToken), Ok);

    [HttpPut("{id:guid}/entries/{contactId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetEntry(Guid id, Guid contactId, ModuleEntryRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(
            new SetModuleEntryCommand(id, contactId, request.Day, request.Month, request.Year, request.ForName), cancellationToken));

    [HttpDelete("{id:guid}/entries/{contactId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveEntry(Guid id, Guid contactId, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new RemoveModuleEntryCommand(id, contactId), cancellationToken));
}
