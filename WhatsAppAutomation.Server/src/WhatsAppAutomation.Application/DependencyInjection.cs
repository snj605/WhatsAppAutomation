using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppAutomation.Application.Common.Behaviors;
using WhatsAppAutomation.Application.Features.Authentication;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Contacts;

namespace WhatsAppAutomation.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg =>
        {
            // MediatR 13+ needs a licence key (free Community edition for small businesses).
            // Without one it still works and only logs a warning. Set MediatR:LicenseKey in user secrets.
            cfg.LicenseKey = configuration["MediatR:LicenseKey"];
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);

            // Pipeline order: logging wraps validation, which wraps the handler.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<SessionStore>();
        services.AddScoped<Features.Members.MemberAccess>();
        services.AddScoped<Features.Members.UserRemoval>();

        services.AddSingleton<BroadcastPacing>();
        services.AddScoped<BroadcastDispatcher>();
        services.AddScoped<WhatsAppNumberChecker>();
        services.AddScoped<Features.Modules.ModuleAutoSender>();
        return services;
    }
}
