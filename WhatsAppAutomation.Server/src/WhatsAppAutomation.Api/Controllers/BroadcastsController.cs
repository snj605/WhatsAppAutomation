using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Broadcasts.Commands;
using WhatsAppAutomation.Application.Features.Broadcasts.Queries;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>The signed-in user's broadcasts: preview, start, pause, resume, cancel, and progress.</summary>
[ApiController]
[Route("api/v1/broadcasts")]
[Produces("application/json")]
public sealed class BroadcastsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<IReadOnlyList<BroadcastSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetBroadcastsQuery(), cancellationToken), Ok);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<BroadcastDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetBroadcastQuery(id), cancellationToken), Ok);

    /// <summary>Who would get which text, and who would be skipped and why. Saves nothing.</summary>
    [HttpPost("preview")]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<BroadcastPreviewDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(BroadcastInput request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new PreviewBroadcastQuery(request), cancellationToken), Ok);

    /// <summary>Creates the broadcast and starts it. Messages then go out one at a time, at the paced rate.</summary>
    [HttpPost]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<BroadcastSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(BroadcastInput request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new CreateBroadcastCommand(request), cancellationToken), Ok);

    [HttpPost("{id:guid}/pause")]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<BroadcastSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pause(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new ChangeBroadcastCommand(id, BroadcastAction.Pause), cancellationToken), Ok);

    [HttpPost("{id:guid}/resume")]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<BroadcastSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new ChangeBroadcastCommand(id, BroadcastAction.Resume), cancellationToken), Ok);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<BroadcastSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new ChangeBroadcastCommand(id, BroadcastAction.Cancel), cancellationToken), Ok);

    /// <summary>Today's count against the daily limit, sending hours, and when the next message may go.</summary>
    [HttpGet("pacing")]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<PacingDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPacing(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetPacingQuery(), cancellationToken), Ok);
}
