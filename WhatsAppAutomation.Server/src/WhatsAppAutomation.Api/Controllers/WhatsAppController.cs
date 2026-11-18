using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Configurations;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.WhatsApp;
using WhatsAppAutomation.Application.Features.WhatsApp.Commands;
using WhatsAppAutomation.Application.Features.WhatsApp.Queries;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>The signed-in user's own WhatsApp number: link, unlink, status, and a test message.</summary>
[ApiController]
[Route("api/v1/whatsapp")]
[Produces("application/json")]
public sealed class WhatsAppController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [Authorize]
    [ProducesResponseType<WhatsAppAccountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetMyWhatsAppQuery(), cancellationToken), Ok);

    [HttpPost("link")]
    [HasPermission(Permissions.WhatsApp.Link)]
    [ProducesResponseType<WhatsAppAccountDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Link(LinkWhatsAppRequest request, CancellationToken cancellationToken)
    {
        LinkMethod? method = request.Method switch
        {
            "qr" => LinkMethod.Qr,
            "code" => LinkMethod.PhoneCode,
            _ => null,
        };
        if (method is null)
        {
            return this.ToProblem(Error.Validation("method must be \"qr\" or \"code\"."));
        }

        var result = await mediator.Send(new StartLinkingCommand(method.Value, request.PhoneNumber), cancellationToken);
        return this.ToActionResult(result, account => Accepted(account));
    }

    /// <summary>Connects the linked number again with its saved login (no new QR), for example after "Stopped".</summary>
    [HttpPost("reconnect")]
    [HasPermission(Permissions.WhatsApp.Link)]
    [ProducesResponseType<WhatsAppAccountDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Reconnect(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new ReconnectWhatsAppCommand(), cancellationToken), account => Accepted(account));

    [HttpPost("unlink")]
    [HasPermission(Permissions.WhatsApp.Link)]
    [ProducesResponseType<WhatsAppAccountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Unlink(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new UnlinkWhatsAppCommand(), cancellationToken), Ok);

    [HttpGet("messages")]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<IReadOnlyList<OutgoingMessageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMessages([FromQuery] int take = 20, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await mediator.Send(new GetMyMessagesQuery(take), cancellationToken), Ok);

    [HttpPost("messages")]
    [HasPermission(Permissions.Messages.Send)]
    [EnableRateLimiting(RateLimitPolicies.Send)]
    [ProducesResponseType<OutgoingMessageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SendTestMessage(SendTestMessageRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SendTestMessageCommand(request.To, request.Text), cancellationToken), Ok);
}
