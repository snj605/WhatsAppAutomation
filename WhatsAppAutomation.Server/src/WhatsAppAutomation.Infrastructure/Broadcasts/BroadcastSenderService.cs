using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Features.Broadcasts;

namespace WhatsAppAutomation.Infrastructure.Broadcasts;

/// <summary>Runs the paced sender every few seconds while the API runs. The pacing rules live in BroadcastDispatcher.</summary>
internal sealed class BroadcastSenderService(
    IServiceScopeFactory scopes, IOptions<BroadcastOptions> options, ILogger<BroadcastSenderService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("The broadcast sender is switched off (Broadcasting:Enabled = false).");
            return;
        }

        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<BroadcastDispatcher>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Keep running: one bad run must not stop all sending. The next run starts fresh.
                logger.LogError(ex, "The broadcast sender failed; it tries again in {Seconds} seconds.", Tick.TotalSeconds);
            }
        }
    }
}
