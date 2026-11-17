using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WhatsAppAutomation.Api.Configurations;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Authentication;
using WhatsAppAutomation.Application.Features.Authentication.Commands;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>
/// Sign-in for the browser app. Same use cases as <see cref="AuthController"/>, but the refresh token
/// never reaches page scripts: it lives in an HttpOnly cookie that only these endpoints receive.
/// Phone apps keep using <c>/api/v1/auth</c> and store the refresh token in secure storage.
/// </summary>
/// <remarks>
/// Cross-site request forgery: the cookies are SameSite=Strict, and every call here must also send
/// the X-XSRF-TOKEN header with the value of the readable XSRF-TOKEN cookie ("double submit").
/// A page on another site can neither read that cookie nor make the browser send the Strict cookies.
/// </remarks>
[ApiController]
[Route("api/v1/auth/web")]
[Produces("application/json")]
public sealed class WebAuthController(ISender mediator) : ControllerBase
{
    internal const string RefreshCookie = "waa_refresh";
    internal const string DeviceCookie = "waa_device";
    internal const string XsrfCookie = "XSRF-TOKEN";
    internal const string XsrfHeader = "X-XSRF-TOKEN";
    private const string CookiePath = "/api/v1/auth/web";
    private const string WebDeviceName = "Web browser";

    /// <summary>Gives the browser an XSRF token before its first sign-in.</summary>
    [HttpGet("xsrf")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Xsrf()
    {
        if (!Request.Cookies.ContainsKey(XsrfCookie))
        {
            SetXsrfCookie();
        }

        return NoContent();
    }

    /// <summary>Signs in with user name and password. The refresh token is set as an HttpOnly cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<WebSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(WebLoginRequest request, CancellationToken cancellationToken)
    {
        if (!HasValidXsrfHeader())
        {
            return XsrfProblem();
        }

        // The browser's device id is ours to create and is kept in its own HttpOnly cookie.
        var deviceId = Request.Cookies[DeviceCookie] is { Length: > 0 and <= 100 } existing
            ? existing
            : NewRandomToken();

        var result = await mediator.Send(
            new LoginCommand(request.UserName, request.Password, deviceId, WebDeviceName), cancellationToken);
        return this.ToActionResult(result, session =>
        {
            SetSessionCookies(session, deviceId);
            return Ok(ToResponse(session));
        });
    }

    /// <summary>Swaps the refresh cookie for a new access token, and rotates the cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<WebSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!HasValidXsrfHeader())
        {
            return XsrfProblem();
        }

        var refreshToken = Request.Cookies[RefreshCookie];
        var deviceId = Request.Cookies[DeviceCookie];
        if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(deviceId))
        {
            return this.ToProblem(AuthErrors.InvalidRefreshToken);
        }

        var result = await mediator.Send(new RefreshCommand(refreshToken, deviceId), cancellationToken);
        if (!result.IsSuccess)
        {
            Response.Cookies.Delete(RefreshCookie, CookieOptionsFor(DateTimeOffset.UnixEpoch));
            return this.ToProblem(result.Error!);
        }

        SetSessionCookies(result.Value, deviceId);
        return Ok(ToResponse(result.Value));
    }

    /// <summary>Ends this browser's session and removes the refresh cookie.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!HasValidXsrfHeader())
        {
            return XsrfProblem();
        }

        if (Request.Cookies[RefreshCookie] is { Length: > 0 } refreshToken)
        {
            await mediator.Send(new LogoutCommand(refreshToken), cancellationToken);
        }

        Response.Cookies.Delete(RefreshCookie, CookieOptionsFor(DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    private static string NewRandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static WebSessionResponse ToResponse(AuthSession session) => new(
        session.AccessToken,
        session.AccessTokenExpiresAt,
        AuthController.ToResponse(session.User));

    private static CookieOptions CookieOptionsFor(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expires,
        IsEssential = true,
    };

    private bool HasValidXsrfHeader()
    {
        var cookie = Request.Cookies[XsrfCookie];
        var header = Request.Headers[XsrfHeader].ToString();
        return !string.IsNullOrEmpty(cookie)
            && !string.IsNullOrEmpty(header)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(header));
    }

    private ObjectResult XsrfProblem()
    {
        // Make sure the browser has a token for its next attempt.
        SetXsrfCookie();
        return this.ToProblem(new Error(
            "auth.xsrf_failed", "The request is missing its security token. Reload the page and try again.",
            ErrorKind.Forbidden));
    }

    private void SetXsrfCookie() =>
        Response.Cookies.Append(XsrfCookie, NewRandomToken(), new CookieOptions
        {
            HttpOnly = false, // Angular reads it and copies it into the X-XSRF-TOKEN header.
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });

    private void SetSessionCookies(AuthSession session, string deviceId)
    {
        Response.Cookies.Append(RefreshCookie, session.RefreshToken, CookieOptionsFor(session.RefreshTokenExpiresAt));
        Response.Cookies.Append(DeviceCookie, deviceId, CookieOptionsFor(DateTimeOffset.UtcNow.AddYears(1)));
    }
}
