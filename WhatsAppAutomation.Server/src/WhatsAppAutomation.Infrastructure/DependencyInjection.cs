using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Authentication;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Infrastructure.Broadcasts;
using WhatsAppAutomation.Infrastructure.Contacts;
using WhatsAppAutomation.Infrastructure.Identity;
using WhatsAppAutomation.Infrastructure.Persistence;
using WhatsAppAutomation.Infrastructure.WhatsApp;

namespace WhatsAppAutomation.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is missing. Set ConnectionStrings__{ConnectionStringName}.");
            }

            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = false;

                // The floor for everyone, with no character-class rules. PasswordRules (Application) adds the rest:
                // obvious passwords are refused, and accounts that manage users or roles need 12 characters.
                options.Password.RequiredLength = 6;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddMemoryCache();
        services.AddSingleton<PermissionCacheSignal>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((jwt, auth) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = auth.Value.Issuer,
                    ValidAudience = auth.Value.Audience,
                    IssuerSigningKey = JwtAccessTokenIssuer.CreateKey(auth.Value.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddSingleton<IContactSpreadsheet, ContactSpreadsheet>();
        services.AddScoped<ISendTurn, PostgresSendTurn>();
        services.AddOptions<BroadcastOptions>()
            .Bind(configuration.GetSection(BroadcastOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.MaxDelaySeconds >= o.MinDelaySeconds && o.BatchRestMaxMinutes >= o.BatchRestMinMinutes, "Maximum values must not be below the minimums.")
            .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Broadcasting:TimeZone is not a known time zone.")
            .ValidateOnStart();
        services.AddHostedService<BroadcastSenderService>();
        services.AddHostedService<WhatsAppNumberCheckService>();
        services.AddHostedService<BirthdayAutoSendService>();

        services.AddOptions<GatewayOptions>().Bind(configuration.GetSection(GatewayOptions.SectionName));
        services.AddHttpClient<IWhatsAppGateway, HttpWhatsAppGateway>((sp, http) =>
        {
            var gateway = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
            http.BaseAddress = gateway.BaseUrl.AbsoluteUri.EndsWith('/') ? gateway.BaseUrl : new Uri(gateway.BaseUrl.AbsoluteUri + "/");

            // Checking the number and sending can take a few seconds on a slow connection; the first message of a
            // broadcast with a file also uploads that file (up to 16 MB) to WhatsApp.
            http.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database")
            .AddCheck<WhatsAppGatewayHealthCheck>("whatsapp-gateway");

        return services;
    }
}
