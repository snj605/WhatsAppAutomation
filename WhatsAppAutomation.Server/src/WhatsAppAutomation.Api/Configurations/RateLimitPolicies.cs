using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WhatsAppAutomation.Api.Configurations;

internal static class RateLimitPolicies
{
    /// <summary>Sign-in and token endpoints: by default 10 requests a minute per client IP.</summary>
    public const string Auth = "auth";

    /// <summary>Sending a message by hand: by default 10 a minute per user.</summary>
    public const string Send = "send";

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimits:AuthPermitsPerMinute", 10);
        var sendLimit = configuration.GetValue("RateLimits:SendPermitsPerMinute", 10);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            options.AddPolicy(Send, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = sendLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
        return services;
    }
}
