using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using WhatsAppAutomation.Application.Common.Interfaces;

namespace WhatsAppAutomation.Api.Realtime;

internal static class RealtimeSetup
{
    public static IServiceCollection AddRealtime(this IServiceCollection services)
    {
        services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();
        services.AddScoped<IWhatsAppNotifier, SignalRWhatsAppNotifier>();

        // Browsers cannot set an Authorization header on a WebSocket, so SignalR sends the access
        // token as ?access_token=… instead. Accept it there, and only for the hubs.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events?.OnMessageReceived;
            options.Events ??= new JwtBearerEvents();
            options.Events.OnMessageReceived = async context =>
            {
                if (previous is not null)
                {
                    await previous(context);
                }

                var token = context.Request.Query["access_token"].ToString();
                if (string.IsNullOrEmpty(context.Token) && token.Length > 0
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = token;
                }
            };
        });
        return services;
    }
}
