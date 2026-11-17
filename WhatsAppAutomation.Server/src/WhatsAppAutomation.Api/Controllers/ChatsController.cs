using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Features.Chats;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>
/// The chat-style views: the Chats list, a group's or category's chat, and today's Birthdays chat. Sending still
/// goes through POST /api/v1/broadcasts (with one group, one category, or forBirthday).
/// </summary>
[ApiController]
[Route("api/v1/chats")]
[Produces("application/json")]
public sealed class ChatsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Broadcasts.Send)]
    [ProducesResponseType<IReadOnlyList<ChatListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetChatsQuery(), cancellationToken), Ok);

    [HttpGet("group/{id:guid}")]
    [HasPermission(Permissions.Broadcasts.Send)]
    [ProducesResponseType<TargetChatDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGroup(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetTargetChatQuery(BroadcastTarget.Group, id), cancellationToken), Ok);

    [HttpGet("category/{id:guid}")]
    [HasPermission(Permissions.Broadcasts.Send)]
    [ProducesResponseType<TargetChatDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCategory(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetTargetChatQuery(BroadcastTarget.Category, id), cancellationToken), Ok);

    [HttpGet("birthdays")]
    [HasPermission(Permissions.Broadcasts.Send)]
    [ProducesResponseType<BirthdaysChatDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBirthdays(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetBirthdaysChatQuery(), cancellationToken), Ok);
}
