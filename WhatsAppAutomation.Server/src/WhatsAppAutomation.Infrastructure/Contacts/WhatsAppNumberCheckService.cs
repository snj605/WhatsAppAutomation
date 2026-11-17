using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Contacts;

namespace WhatsAppAutomation.Infrastructure.Contacts;

/// <summary>
/// Runs the WhatsApp number check every few seconds while the API runs (see WhatsAppNumberChecker). Switched off
/// together with the broadcast sender (Broadcasting:Enabled = false), as in the automated tests.
/// </summary>
internal sealed class WhatsAppNumberCheckService(
    IServiceScopeFactory scopes, IOptions<BroadcastOptions> options, ILogger<WhatsAppNumberCheckService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WhatsAppNumberChecker>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The WhatsApp number check failed; it tries again in {Seconds} seconds.", Tick.TotalSeconds);
            }
        }
    }
}
