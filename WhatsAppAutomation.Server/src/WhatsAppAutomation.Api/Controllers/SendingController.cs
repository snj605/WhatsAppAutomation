using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.Sending;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>How the signed-in user's number sends: messages a day, chunk size, rest, gaps and sending hours.</summary>
[ApiController]
[Route("api/v1/sending/settings")]
[Produces("application/json")]
[HasPermission(Permissions.Messages.Send)]
public sealed class SendingController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SendingSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetSendingSettingsQuery(), cancellationToken), Ok);

    [HttpPut]
    [ProducesResponseType<SendingSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Set(SetSendingSettingsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SetSendingSettingsCommand(
            request.DailyLimit, request.ChunkSize, request.ChunkRestMinutes, request.SendFrom, request.SendUntil,
            request.MinGapSeconds, request.MaxGapSeconds), cancellationToken), Ok);
}

/// <param name="DailyLimit">300 to 500 messages a day.</param>
/// <param name="SendFrom">"HH:mm". The same as SendUntil means any time.</param>
public sealed record SetSendingSettingsRequest(
    int DailyLimit, int ChunkSize, int ChunkRestMinutes, string SendFrom, string SendUntil, int MinGapSeconds, int MaxGapSeconds);
