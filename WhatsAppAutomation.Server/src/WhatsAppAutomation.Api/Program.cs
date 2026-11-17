using System.Text.Json;
using System.Text.Json.Serialization;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Cli;
using WhatsAppAutomation.Api.Configurations;
using WhatsAppAutomation.Api.Development;
using WhatsAppAutomation.Api.Realtime;
using WhatsAppAutomation.Application;
using WhatsAppAutomation.Infrastructure;

namespace WhatsAppAutomation.Api
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            if (AdminCli.TryRunWithoutHost(args, Console.Out) is { } exitCode)
            {
                return exitCode;
            }

            var isCli = AdminCli.IsCliCommand(args);
            var builder = WebApplication.CreateBuilder(isCli ? [] : args);
            if (isCli)
            {
                builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            }

            builder.Services.AddApplication(builder.Configuration);
            builder.Services.AddInfrastructure(builder.Configuration);
            builder.Services.AddApiRateLimits(builder.Configuration);
            builder.Services.AddProblemDetails();
            builder.Services.AddPermissionAuthorization();

            // Enums travel as text such as "not_linked", the same names the WhatsApp gateway uses.
            var enumsAsText = new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower);
            builder.Services.AddControllers()
                .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(enumsAsText));
            builder.Services.AddSignalR()
                .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(enumsAsText));
            builder.Services.AddRealtime();

            if (builder.Environment.IsDevelopment())
            {
                builder.Services.Configure<GatewayDevOptions>(builder.Configuration.GetSection(GatewayDevOptions.SectionName));
                builder.Services.AddHostedService<GatewayDevLauncher>();
            }

            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            var app = builder.Build();

            if (isCli)
            {
                return await AdminCli.RunAsync(app.Services, args, Console.Out, Console.In);
            }

            app.UseExceptionHandler();
            app.UseStatusCodePages();

            app.UseDefaultFiles();
            app.MapStaticAssets();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }
            else
            {
                app.UseHsts();
            }

            // The gateway calls /internal over plain http on the private network; everything else moves to https.
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments("/internal"),
                branch => branch.UseHttpsRedirection());

            app.UseAuthentication();
            app.UseRateLimiter();
            app.UseAuthorization();

            app.MapControllers();
            app.MapHub<WhatsAppHub>(WhatsAppHub.Path);
            app.MapHealthChecks("/health");

            app.MapFallbackToFile("/index.html");

            await app.RunAsync();
            return 0;
        }
    }
}
