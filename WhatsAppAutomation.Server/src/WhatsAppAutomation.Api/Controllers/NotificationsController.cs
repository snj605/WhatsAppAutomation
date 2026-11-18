using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.Notifications;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>The signed-in user's notifications (new ones also arrive live as the "notification" event).</summary>
[ApiController]
[Route("api/v1/notifications")]
[Produces("application/json")]
[Authorize]
public sealed class NotificationsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<NotificationsPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetNotificationsQuery(), cancellationToken), Ok);

    /// <summary>Marks the listed notifications as read, or all when the list is empty or missing.</summary>
    [HttpPost("read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(MarkReadRequest? request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new MarkNotificationsReadCommand(request?.Ids), cancellationToken));
}

public sealed record MarkReadRequest(IReadOnlyList<Guid>? Ids);
