using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WhatsAppAutomation.Api.Configurations;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Authentication;
using WhatsAppAutomation.Application.Features.Authentication.Commands;
using WhatsAppAutomation.Application.Features.Authentication.Queries;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>
/// Controllers stay thin: turn the HTTP request into a command or query, send it through
/// MediatR, and turn the result into an HTTP response. All logic lives in the handlers.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(ISender mediator) : ControllerBase
{
    /// <summary>Signs in with user name and password and starts a session for this device.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new LoginCommand(request.UserName, request.Password, request.DeviceId, request.DeviceName), cancellationToken);
        return this.ToActionResult(result, session => Ok(ToResponse(session)));
    }

    /// <summary>Swaps a refresh token for a new access token and a new refresh token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RefreshCommand(request.RefreshToken, request.DeviceId), cancellationToken);
        return this.ToActionResult(result, session => Ok(ToResponse(session)));
    }

    /// <summary>Ends the session on this device.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new LogoutCommand(request.RefreshToken), cancellationToken));

    /// <summary>Who is signed in and what they may do. Apps call this at start-up.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCurrentUserQuery(), cancellationToken);
        return this.ToActionResult(result, me => Ok(new MeResponse(ToResponse(me.User), me.Permissions)));
    }

    private static SessionResponse ToResponse(AuthSession session) => new(
        session.AccessToken,
        session.AccessTokenExpiresAt,
        session.RefreshToken,
        session.RefreshTokenExpiresAt,
        ToResponse(session.User));

    private static UserResponse ToResponse(UserSummary user) =>
        new(user.Id, user.UserName, user.DisplayName, user.Roles);
}
