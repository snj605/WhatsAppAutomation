using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.WhatsApp.Commands;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>
/// Called only by the WhatsApp gateway, never by apps. Protected by the shared Gateway:Secret
/// instead of a user token. In production the reverse proxy must not forward /internal at all.
/// </summary>
[ApiController]
[Route("internal/whatsapp")]
[AllowAnonymous]
[GatewaySecret]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalWhatsAppController(ISender mediator) : ControllerBase
{
    [HttpPost("events")]
    public async Task<IActionResult> Event(GatewayEventRequest e, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new HandleGatewayEventCommand(e.Type, e.AccountId, e.Qr, e.Code, e.Status, e.Reason, e.PhoneNumber, e.Name, e.Reference, e.At),
            cancellationToken);
        return this.ToActionResult(result);
    }
}
