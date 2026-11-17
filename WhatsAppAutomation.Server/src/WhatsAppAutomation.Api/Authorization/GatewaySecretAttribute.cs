using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Infrastructure.WhatsApp;

namespace WhatsAppAutomation.Api.Authorization;

/// <summary>
/// Allows the request only when it carries the shared Gateway:Secret in X-Gateway-Secret. Used on
/// the endpoints the WhatsApp gateway calls, which have no signed-in user.
/// </summary>
public sealed class GatewaySecretAttribute() : TypeFilterAttribute(typeof(GatewaySecretFilter));

internal sealed class GatewaySecretFilter(IOptions<GatewayOptions> options) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var secret = options.Value.Secret;
        if (!options.Value.IsConfigured
            || !context.HttpContext.Request.Headers.TryGetValue(GatewayOptions.SecretHeader, out var provided)
            || !SecretsEqual(provided.ToString(), secret))
        {
            context.Result = new UnauthorizedResult();
        }
    }

    /// <summary>Constant-time comparison, so the response time does not reveal how much of a guess was right.</summary>
    private static bool SecretsEqual(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
